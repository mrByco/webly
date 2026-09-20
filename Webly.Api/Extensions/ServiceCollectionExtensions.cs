using Webly.Api.Infrastructure;
using Webly.Api.Options;
using Webly.Services.Services.Authentication;
using Webly.Services.Agent;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sandboxes;
using Webly.Services.Services.Email;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Text.Json;
using System.Text;

namespace Webly.Api.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The scheme used to carry an OAuth handshake and nothing else. It exists because the external
    /// provider has to sign a principal in somewhere before we have decided which Webly account it
    /// maps to; the callback reads it, converts it into our own tokens, and deletes it.
    /// </summary>
    public const string ExternalScheme = "External";

    public static IServiceCollection AddWeblyAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailTokenOptions>()
            .Bind(configuration.GetSection(EmailTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Not validated at startup: an empty administrator list is a legitimate deployment.
        services.AddOptions<AdministratorOptions>()
            .Bind(configuration.GetSection(AdministratorOptions.SectionName));

        var googleOptions = new GoogleAuthOptions();
        configuration.GetSection(GoogleAuthOptions.SectionName).Bind(googleOptions);
        services.Configure<GoogleAuthOptions>(configuration.GetSection(GoogleAuthOptions.SectionName));

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException($"Missing '{JwtOptions.SectionName}' configuration.");

        var authentication = services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Off, so the claims that come back out are spelled the way they were minted.
                // Leaving it on rewrites short names into long schema URIs and quietly breaks any
                // lookup by the original name.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                // The one thing signature validation cannot tell us: whether this token was
                // withdrawn after it was issued. Checked here rather than per controller so a new
                // endpoint cannot forget to.
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var tokenId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);
                        var userId = context.Principal.GetUserIdUnverified();
                        var emailVerified = context.Principal.IsEmailVerified();

                        var blacklist = context.HttpContext.RequestServices
                            .GetRequiredService<IAccessTokenBlacklist>();

                        if (tokenId is not null && userId is not null
                            && blacklist.IsRevoked(tokenId, userId.Value, emailVerified))
                        {
                            context.Fail("This access token was revoked.");
                        }

                        return Task.CompletedTask;
                    }
                };
            })
            .AddCookie(ExternalScheme);

        // Registered only when credentials exist. Calling AddGoogle with a null client id throws at
        // startup, so an unconfigured environment would not boot at all.
        if (googleOptions.IsConfigured)
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = googleOptions.ClientId!;
                options.ClientSecret = googleOptions.ClientSecret!;
                options.SignInScheme = ExternalScheme;

                // Google's default claim mapping drops both of these. `email_verified` is not
                // optional for us — without it every external sign-in looks unverified and is
                // refused, because linking on an unconfirmed address is how accounts get stolen.
                options.Events.OnCreatingTicket = context =>
                {
                    CopyClaim(context.Identity, context.User, "email_verified");
                    CopyClaim(context.Identity, context.User, "picture");

                    return Task.CompletedTask;
                };
            });
        }

        services.AddWeblyRateLimiting();

        services.AddAuthorization(options =>
        {
            // Default deny. Anything without an explicit [AllowAnonymous] requires a signed-in
            // user, so forgetting to protect a new endpoint fails closed rather than open.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    /// <summary>
    /// Chooses the mail transport. Resend when an API key is configured, otherwise the logging
    /// sender — so development and CI work with no credentials, and a deployment that forgets the
    /// key writes emails to its log rather than silently dropping them.
    /// </summary>
    public static IServiceCollection AddWeblyEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new EmailOptions();
        configuration.GetSection(EmailOptions.SectionName).Bind(options);

        if (options.Resend.IsConfigured)
            services.AddHttpClient<IEmailSender, ResendEmailSender>();
        else
            services.AddSingleton<IEmailSender, LoggingEmailSender>();

        return services;
    }

    private static IServiceCollection AddWeblyRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Per IP rather than per address: the address is attacker-supplied, so limiting on it
            // alone would let someone walk through a list of victims unimpeded. The per-address
            // cooldown in the use cases is the other half of this.
            options.AddPolicy(RateLimitPolicies.Mail, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0
                    }));

            // Per user, not per IP: reaching this already takes a verified account, and an office behind one NAT must
            // not share a single budget. See RateLimitPolicies for what it is fencing.
            options.AddPolicy(RateLimitPolicies.Deploy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionByUser(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0
                    }));
        });

    /// <summary>
    /// Everything a site's source and its hosting need: where repositories live, where the starter template is,
    /// which sandbox provider runs the agents, and the deployment provider.
    ///
    /// The options classes that would make the app useless if wrong are validated at startup; the ones whose
    /// absence is a legitimate deployment (a sandbox vendor's key, a Vercel token, an agent's key) are not — see
    /// CLAUDE.md, "An unconfigured feature is absent, not broken".
    /// </summary>
    public static IServiceCollection AddWeblySites(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SitesOptions>()
            .Bind(configuration.GetSection(SitesOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RepositoryOptions>()
            .Bind(configuration.GetSection(RepositoryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<TemplateOptions>()
            .Bind(configuration.GetSection(TemplateOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SandboxOptions>().Bind(configuration.GetSection(SandboxOptions.SectionName));
        services.AddOptions<CodingAgentOptions>().Bind(configuration.GetSection(CodingAgentOptions.SectionName));
        services.AddOptions<DeploymentOptions>().Bind(configuration.GetSection(DeploymentOptions.SectionName));

        // One named client for every call into a sandbox — the provider's control API and, once one is running,
        // the sandbox agent itself. Twenty minutes because `/exec` streams for as long as the command runs, and
        // `npm install` in a cold workspace is minutes.
        //
        // Named rather than typed: the providers are singletons, and a typed client captured by a singleton is
        // the documented way to keep one handler for the life of the process. They ask the factory per sandbox
        // instead.
        services.AddHttpClient(SandboxAgentClient.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(20));

        // Both providers are registered as themselves so either can be resolved in a test; only the selected one
        // is the ISandboxProvider the app uses.
        services.AddSingleton<E2bSandboxProvider>();
        services.AddSingleton<DockerSandboxProvider>();

        var provider = configuration[$"{SandboxOptions.SectionName}:Provider"] ?? DockerSandboxProvider.ProviderName;

        if (string.Equals(provider, E2bSandboxProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ISandboxProvider>(x => x.GetRequiredService<E2bSandboxProvider>());
        else
            services.AddSingleton<ISandboxProvider>(x => x.GetRequiredService<DockerSandboxProvider>());

        services.AddHttpClient<IDeploymentTarget, VercelDeploymentTarget>(client =>
        {
            client.BaseAddress = new Uri("https://api.vercel.com");
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        // The preview proxy's own client: no timeout of its own, because a dev server compiling a page on first
        // request can take a minute and YARP's ActivityTimeout is what bounds it. AllowAutoRedirect off, so a
        // redirect from the site is passed to the browser rather than followed server-side into the sandbox.
        services.AddHttpClient(nameof(Controllers.PreviewController))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(15)
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        services.AddHttpForwarder();

        return services;
    }

    /// <summary>
    /// The partition key for a per-user limiter. Falls back to the connection address for a caller with no id, which
    /// cannot happen on these endpoints (they are behind the default-deny policy) but must not partition everybody
    /// into one bucket called "null" if it ever does.
    /// </summary>
    private static string PartitionByUser(HttpContext context) =>
        context.User.GetUserIdUnverified()?.ToString()
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";

    private static void CopyClaim(ClaimsIdentity? identity, JsonElement payload, string name)
    {
        if (identity is not null && payload.TryGetProperty(name, out var value))
            identity.AddClaim(new Claim(name, value.ToString()));
    }
}
