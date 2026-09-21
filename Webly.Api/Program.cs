using Webly.Api.Extensions;
using Webly.Api.Hubs;
using Webly.Api.Infrastructure;
using Webly.Api.Middleware;
using Webly.Data;
using Webly.Services;
using Webly.Services.Services.Realtime;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sandboxes;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

// The content root is the application's own directory, not whatever directory somebody launched it from.
// `WebApplication.CreateBuilder` defaults it to the current directory, and that default has no correct value
// here: `dotnet run` uses the project's directory while the dev MCP server runs the built exe from the
// repository root, so one of the two would always fail to find appsettings.json. Anchoring it to the binary
// makes both work, and production — where the published app and its configuration sit in /app together — is
// the same case.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// The provider keys live here in development, never in appsettings.json — that file is in git.
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddControllers()
    // Enums travel as their names, not their positions, and the database stores them as text for the
    // same reason. The load-bearing ones are SiteVersionOrigin, DeploymentStatus and RunEventType:
    // each is a generated TypeScript union the client switches on, so a numbered enum would make
    // inserting a value in the middle a silent breaking change in two places at once.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// The hub's JSON has to agree with the controllers': the client deserializes RunEventEnvelope with the same generated
// types as every REST response, and a hub that numbered its enums would hand it values its union cannot express.
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSwaggerGen(options =>
{
    // Nullable reference types are the DTOs' documentation, and without this line the document throws all of
    // it away: every `string` is described as nullable and nothing is required, so the generated TypeScript
    // types every field as `string | null`. The client then either fills itself with `!` and `?? ''` — which
    // is a hundred assertions standing in for a contract — or stops type-checking against the API at all.
    // `Nullable` is enabled solution-wide precisely so that `string` and `string?` mean different things; this
    // is what carries the difference across the wire.
    options.SupportNonNullableReferenceTypes();

    // The hub's own types, which no controller references and Swagger therefore never sees. See
    // HubContractDocumentFilter: without it the client hand-declares the realtime contract, and a value added
    // to RunEventType here changes nothing there until somebody remembers.
    options.DocumentFilter<HubContractDocumentFilter>();
});
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// The five settings that name something on disk, made absolute before anything reads them. They are written
// relative in configuration because "templates/next-site" is what they mean, and a relative path otherwise
// resolves against the working directory — which is not a thing this app gets to choose. See PathAnchor for
// what they are resolved against and why it is not the content root.
//
// Deployment:FileSystem:Root is on the list because it was the fourth one to be found the hard way: a publish
// reported Ready, wrote its export under Webly.Api/, and the URL in the row 404ed. A deployment that says it
// succeeded and serves nothing is the worst outcome this product has.
PathAnchor.Resolve(builder.Configuration, builder.Environment,
    "Templates:SitePath",
    "Sandbox:Local:AgentPath",
    "Sandbox:Local:WorkspaceRoot",
    "Repositories:Root",
    "Deployment:FileSystem:Root");

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
builder.Services.AddWeblySites(builder.Configuration, builder.Environment);
builder.Services.AddWeblyRealtime();
builder.Services.AddWeblyAgents();

// The publisher lives up here because it is the only piece of the run substrate that knows about the transport.
builder.Services.AddSingleton<IRunPublisher, SignalRRunPublisher>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WeblyDbContext>();
    await db.Database.MigrateAsync();

    // Created here rather than on first use: a missing repository root is a deployment mistake (an
    // unmounted volume), and finding that out at boot beats finding it out when somebody creates
    // their first site.
    Directory.CreateDirectory(
        scope.ServiceProvider.GetRequiredService<IOptions<RepositoryOptions>>().Value.Root);

    // The same argument, for the two paths that cannot be created because they have to contain something.
    // Both have already been made absolute by PathAnchor, so what is left to check is whether the thing is
    // really there — and the alternative to checking is a first site creation that fails for a missing
    // template and a first agent turn that fails for a missing sandbox agent, neither of which names a path.
    var templatePath = scope.ServiceProvider.GetRequiredService<IOptions<TemplateOptions>>().Value.SitePath;

    if (!Directory.Exists(templatePath))
        throw new InvalidOperationException(
            $"Templates:SitePath '{templatePath}' does not exist. Every new site starts as a copy of it: it is "
            + "templates/next-site in the repository, and the container copies it next to the published app.");

    var sandboxOptions = scope.ServiceProvider.GetRequiredService<IOptions<SandboxOptions>>().Value;

    if (string.Equals(sandboxOptions.Provider, LocalSandboxProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
    {
        var agentPath = scope.ServiceProvider.GetRequiredService<IOptions<LocalSandboxOptions>>().Value.AgentPath;

        if (!File.Exists(agentPath))
            throw new InvalidOperationException(
                $"Sandbox:Local:AgentPath '{agentPath}' does not exist. It is the sandbox agent every turn runs "
                + "through, tools/sandbox-agent/index.js in the repository.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwagger();
    app.UseSwaggerUI();

    // Locally published sites, served from where FileSystemDeploymentTarget wrote them, so that "publish" ends
    // at a page somebody can actually open rather than at a row saying Ready. Development only: in production
    // the provider serves the site and this path does not exist.
    //
    // Static files rather than a controller, because a static export is exactly what a static file middleware is
    // for — and UseDefaultFiles is what makes /published/{slug}/about/ find its index.html, which is the
    // convention `trailingSlash: true` in the template's next.config.ts produces.
    var publishedRoot = Path.GetFullPath(
        app.Services.GetRequiredService<IOptions<DeploymentOptions>>().Value.FileSystem.Root);

    Directory.CreateDirectory(publishedRoot);

    app.Logger.LogInformation("Serving locally published sites from {Root} at /published.", publishedRoot);

    var publishedFiles = new PhysicalFileProvider(publishedRoot);

    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = publishedFiles, RequestPath = "/published" });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = publishedFiles, RequestPath = "/published" });

    // A mistyped address on a published site, answered by that site's own 404 page.
    //
    // Without this it falls past the static files to `MapReverseProxy`, which is a catch-all — so a visitor who
    // typed one character wrong on somebody's shop website landed on **Webly's dashboard**, or on Webly's login
    // page if they were not signed in. Found by opening one; the site's own 404.html had been sitting in the
    // export the whole time, which is exactly why the template has one.
    //
    // Middleware rather than an endpoint, because it has to run after the static files and before routing
    // selects the proxy. Nothing under /published/ is Webly's, so nothing under it falls through: a page of
    // the site when the site has one, and a plain 404 otherwise — which is what a **deleted** site answers,
    // rather than Webly's login page, and that is the second half of taking a site off the internet.
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith("/published/", StringComparison.Ordinal))
        {
            await next();

            return;
        }

        var site = path["/published/".Length..].Split('/')[0];
        var page = Path.Combine(publishedRoot, site, "404.html");

        context.Response.StatusCode = StatusCodes.Status404NotFound;

        // Path.Combine with a `..` in the segment would walk out of the root, so the file has to be inside it
        // — the same rule the repository store applies to a tree, for the same reason.
        if (site.Length > 0
            && Path.GetFullPath(page).StartsWith(publishedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && File.Exists(page))
        {
            context.Response.ContentType = "text/html; charset=utf-8";

            await context.Response.SendFileAsync(page);
        }
    });
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

// Explicit, and this is the line that makes the static files above work at all.
//
// `WebApplication` inserts `UseRouting()` at the *front* of the pipeline when nothing has called it, which puts
// endpoint selection before every middleware here. `MapReverseProxy` is a catch-all, so an endpoint is then
// selected for every request — and `StaticFileMiddleware` deliberately stands down when one already is, on the
// reasonable grounds that a matched endpoint is a more specific answer than a file. The result was that a
// published site 502ed through the proxy to the Angular dev server while its index.html sat on disk, with no
// error anywhere: the file provider resolved it, the middleware was in the pipeline, and it skipped every time.
// Calling `UseRouting` here suppresses that insertion and puts selection back after the files.
app.UseRouting();

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
