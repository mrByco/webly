using Webly.Api.Extensions;
using Webly.Api.Hubs;
using Webly.Api.Infrastructure;
using Webly.Api.Middleware;
using Webly.Data;
using Webly.Services;
using Webly.Services.Services.Realtime;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// The provider keys live here in development, never in appsettings.json — that file is in git.
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddControllers()
    // Enums travel as their names, not their positions, and the database stores them as text for
    // the same reason. SectionType is the load-bearing one: it is the discriminator for every
    // section in a site document, a generated TypeScript union, and the key the renderer switches
    // on — a numbered enum would make inserting a value in the middle a silent data migration.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// The hub's JSON has to agree with the controllers': the client deserializes RunEventEnvelope with the same generated
// types as every REST response, and a hub that numbered its enums would hand it values its union cannot express.
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSwaggerGen();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Resolve the dev DB up front so both the DbContext and the /health endpoint agree on which
// Postgres instance is actually in use. See DevDatabaseResolver for the hybrid strategy.
var configuredConnectionString = builder.Configuration.GetConnectionString("WeblyDb")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:WeblyDb configuration.");

string resolvedConnectionString;
string dbSource;

if (builder.Environment.IsDevelopment())
{
    var startupLogger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Startup");
    (resolvedConnectionString, dbSource) = await DevDatabaseResolver.ResolveAsync(configuredConnectionString, startupLogger);
}
else
{
    resolvedConnectionString = configuredConnectionString;
    dbSource = "configured";
}

builder.Configuration["Database:ActiveSource"] = dbSource;

builder.Services.AddDbContext<WeblyDbContext>(options => options.UseNpgsql(resolvedConnectionString));

builder.Services.AddWeblyServices();
builder.Services.AddWeblyEmail(builder.Configuration);
builder.Services.AddWeblyAuthentication(builder.Configuration);
builder.Services.AddWeblyDeployment(builder.Configuration);
builder.Services.AddWeblyRealtime();
builder.Services.AddWeblyAgent();
builder.Services.AddWeblyAgentClients(builder.Configuration);

// The publisher lives up here because it is the only piece of the run substrate that knows about the transport.
builder.Services.AddSingleton<IRunPublisher, SignalRRunPublisher>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WeblyDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Everything in front of this app in a deployment terminates TLS and forwards over plain HTTP
// from one address, so without this two things break quietly rather than loudly. UseHttpsRedirection
// below would see an HTTP request and try to bounce it, and the mail rate limiter partitions on
// Connection.RemoteIpAddress — which would be the proxy for every visitor alike, collapsing a
// per-IP budget into a single global one that the first person to request a password reset spends.
//
// KnownIPNetworks and KnownProxies are cleared because the proxy's address is assigned by Docker at
// container start and is not knowable here. That is safe only because nothing but the proxy can
// reach the container's port: if the API is ever published directly, this has to name the hop.
if (!app.Environment.IsDevelopment())
{
    var forwardedHeaderOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };

    forwardedHeaderOptions.KnownIPNetworks.Clear();
    forwardedHeaderOptions.KnownProxies.Clear();

    app.UseForwardedHeaders(forwardedHeaderOptions);
}

app.UseHttpsRedirection();

// Outermost of ours, so it can translate anything thrown further in — notably the
// "verify your email first" case — into a proper status code.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Must run before UseAuthentication: it turns the auth cookies into the Authorization header that
// the bearer handler reads, and refreshes an expired session on the way past.
app.UseMiddleware<CookieAuthenticationMiddleware>();

app.UseAuthentication();

// Only the endpoints carrying [EnableRateLimiting] are affected, so the reverse-proxied Angular app
// is untouched — a global limiter here would have the app throttling its own asset requests.
app.UseRateLimiter();

app.UseAuthorization();

// No CORS: the frontend is always reached through this same origin (ng serve proxied in dev, the
// built Angular SSR node server proxied in prod). If a third client ever needs the API directly,
// add CORS deliberately then — don't add it back "just in case".

app.MapControllers();

// Before MapReverseProxy, which is a catch-all. Endpoint routing prefers the literal path regardless, but the order
// says what is intended rather than relying on that.
app.MapHub<RealtimeHub>("/hubs/realtime", options =>
{
    // The browser cannot set an Authorization header on a WebSocket upgrade, so the hub is reached with cookies —
    // which the CookieAuthenticationMiddleware above has already turned into one, because a handshake is an HTTP
    // request like any other. This forces a reconnect when the access token dies, and the reconnect runs that
    // middleware again: without it a long-lived connection outlives its own token and keeps a session alive past
    // logout.
    options.CloseOnAuthenticationExpiration = true;
});

// Everything that isn't an API/health/openapi route falls through to the Angular app: `ng serve`
// in dev, the built SSR node server in prod. Cluster destinations are environment-specific in
// appsettings.{Environment}.json.
//
// AllowAnonymous is load-bearing: authorization defaults to deny, and without this the login page
// itself would require you to be logged in to see it.
app.MapReverseProxy().AllowAnonymous();

app.Run();

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> in the test project can find the entry point.
/// Top-level statements generate an internal Program class, which the factory cannot reach.
/// </summary>
public partial class Program;
