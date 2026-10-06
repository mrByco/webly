using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Webly.Services.Services.Usage;
using Webly.Api.Infrastructure;
using Webly.Api.Options;
using Webly.Services.Services.Authentication;
using Webly.Services.Agent;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sandboxes;
using Webly.Services.Services;
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

        // Validated at startup because three things break quietly without it: verification links, password-reset
        // links, and the form action baked into every published site.
        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
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
                        var sessionId = context.Principal?.FindFirstValue(JwtTokenService.SessionIdClaim);
                        var userId = context.Principal.GetUserIdUnverified();
                        var emailVerified = context.Principal.IsEmailVerified();

                        var blacklist = context.HttpContext.RequestServices
                            .GetRequiredService<IAccessTokenBlacklist>();

                        if (tokenId is not null && userId is not null
                            && blacklist.IsRevoked(tokenId, userId.Value, emailVerified))
                        {
                            context.Fail("This access token was revoked.");
                        }

                        // The session as well as the token: ending every session of a user — a password reset —
                        // names sessions, not the ids of the access tokens each of them happens to hold.
                        if (sessionId is not null && blacklist.IsSessionRevoked(sessionId))
                            context.Fail("This session has ended.");

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

        services.AddWeblyRateLimiting(configuration);

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

    private static IServiceCollection AddWeblyRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            // Per IP rather than per address: the address is attacker-supplied, so limiting on it
            // alone would let someone walk through a list of victims unimpeded. The per-address
            // cooldown in the use cases is the other half of this.
            options.AddPolicy(RateLimitPolicies.Mail, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => Limits(context).Mail.ToLimiterOptions()));

            options.AddPolicy(RateLimitPolicies.SignUp, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => Limits(context).SignUp.ToLimiterOptions()));

            // Per IP, for the reason RateLimitPolicies.SignIn gives: the account is the one thing here the caller
            // chooses, so a limit on it would be a way to lock somebody else out.
            options.AddPolicy(RateLimitPolicies.SignIn, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => Limits(context).SignIn.ToLimiterOptions()));

            // Per user, not per IP: reaching this already takes a verified account, and an office behind one NAT must
            // not share a single budget. See RateLimitPolicies for what it is fencing.
            // Per IP *and* per site — the path is the site, since it is /api/public/forms/{nanoid} and nothing
            // else. Both halves matter: per IP alone would mean a busy office sending one enquiry to one
            // customer's site used up the budget of everyone behind that NAT writing to every other customer's,
            // and per site alone would be a budget one bot could spend on a shop's behalf. A bot walking one
            // site's form still pays within the minute, and SubmitForm's per-site caps are what a thousand hosts
            // sending one submission each run into.
            options.AddPolicy(RateLimitPolicies.Forms, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}|{context.Request.Path}",
                    _ => Limits(context).Forms.ToLimiterOptions()));

            options.AddPolicy(RateLimitPolicies.Deploy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionByUser(context),
                    _ => Limits(context).Deploy.ToLimiterOptions()));
        });
    }

    /// <summary>
    /// Read when a partition is first made rather than once at startup, because the test host adds its configuration
    /// after <c>Program.cs</c> has run — a value captured here would never see it.
    /// </summary>
    private static RateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    /// <summary>
    /// Everything a site's source and its hosting need: where repositories live, where the starter template is,
    /// which sandbox provider runs the agents, and the deployment provider.
    ///
    /// The options classes that would make the app useless if wrong are validated at startup; the ones whose
    /// absence is a legitimate deployment (a sandbox vendor's key, a Vercel token, an agent's key) are not — see
    /// CLAUDE.md, "An unconfigured feature is absent, not broken".
    /// </summary>
    /// <summary>
    /// Sites: where their source lives, where the agent runs, and how they are published.
    ///
    /// Takes the environment as well as the configuration because of one decision: the local sandbox provider
    /// is not isolation, so selecting it outside Development is refused here rather than trusted to a comment.
    /// </summary>
    public static IServiceCollection AddWeblySites(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
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
        services.AddOptions<LocalSandboxOptions>().Bind(configuration.GetSection(LocalSandboxOptions.SectionName));
        services.AddOptions<CodingAgentOptions>().Bind(configuration.GetSection(CodingAgentOptions.SectionName));

        // A Claude subscription is one person's, so it runs that person's own turns and nobody else's. Refused at
        // boot rather than trusted to a comment, like the local sandbox below: a production deployment running every
        // customer's turns on a developer's subscription would work perfectly right up until it was a breach.
        if (!environment.IsDevelopment()
            && !string.IsNullOrWhiteSpace(configuration[$"{CodingAgentOptions.SectionName}:ClaudeCode:OAuthToken"]))
            throw new InvalidOperationException(
                "Agent:ClaudeCode:OAuthToken is a Claude subscription, for a developer's own testing only. "
                + "Use Agent:ClaudeCode:ApiKey outside Development.");

        services.AddOptions<DeploymentOptions>().Bind(configuration.GetSection(DeploymentOptions.SectionName));

        // One named client for every call into a sandbox — the provider's control API and, once one is running,
        // the sandbox agent itself. Twenty minutes because `/exec` streams for as long as the command runs, and
        // `npm install` in a cold workspace is minutes.
        //
        // Named rather than typed: the providers are singletons, and a typed client captured by a singleton is
        // the documented way to keep one handler for the life of the process. They ask the factory per sandbox
        // instead.
        services.AddHttpClient(SandboxAgentClient.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(20));

        // All three providers are registered as themselves so any can be resolved in a test; only the selected
        // one is the ISandboxProvider the app uses.
        services.AddSingleton<E2bSandboxProvider>();
        services.AddSingleton<DockerSandboxProvider>();
        services.AddSingleton<LocalSandboxProvider>();

        var provider = configuration[$"{SandboxOptions.SectionName}:Provider"] ?? LocalSandboxProvider.ProviderName;

        if (string.Equals(provider, E2bSandboxProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ISandboxProvider>(x => Metered(x, x.GetRequiredService<E2bSandboxProvider>()));
        }
        else if (string.Equals(provider, DockerSandboxProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ISandboxProvider>(x => Metered(x, x.GetRequiredService<DockerSandboxProvider>()));
        }
        else if (string.Equals(provider, LocalSandboxProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            // Refused rather than warned about. The local provider runs a coding agent as this process's own
            // user with no isolation at all, so in production it would hand somebody's website's agent the
            // database credentials — a crash at boot is the only safe way to be wrong about this.
            if (!environment.IsDevelopment())
                throw new InvalidOperationException(
                    "Sandbox:Provider 'local' is a development-only provider and offers no isolation. "
                    + "Use 'e2b' outside Development.");

            services.AddSingleton<ISandboxProvider>(x => Metered(x, x.GetRequiredService<LocalSandboxProvider>()));
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown Sandbox:Provider '{provider}'. Expected 'local', 'docker' or 'e2b'.");
        }

        // The publishing target. Vercel is a typed client because it is an HTTP API; the filesystem one is not,
        // because it only needs the sandbox and a directory.
        var deploymentProvider = configuration[$"{DeploymentOptions.SectionName}:Provider"] ?? "vercel";

        if (string.Equals(deploymentProvider, FileSystemDeploymentTarget.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            // Same rule as the local sandbox: refused rather than warned about. A production deployment that
            // silently published into a container's filesystem would report success and serve nothing.
            if (!environment.IsDevelopment())
                throw new InvalidOperationException(
                    "Deployment:Provider 'filesystem' is development-only — it publishes to a local directory. "
                    + "Use 'vercel' outside Development.");

            services.AddSingleton<IDeploymentTarget, FileSystemDeploymentTarget>();
        }
        else if (string.Equals(deploymentProvider, "vercel", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IDeploymentTarget, VercelDeploymentTarget>(client =>
            {
                client.BaseAddress = new Uri("https://api.vercel.com");
                client.Timeout = TimeSpan.FromMinutes(2);
            });
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown Deployment:Provider '{deploymentProvider}'. Expected 'vercel' or 'filesystem'.");
        }

        // The preview proxy's outbound client. A singleton HttpMessageInvoker, not a named HttpClient — see
        // PreviewForwarder, which is where the reason lives and where it is long enough to be worth its own file.
        services.AddSingleton<PreviewForwarder>();

        services.AddHttpForwarder();

        return services;
    }

    /// <summary>
    /// Whichever provider configuration chose, wrapped so every sandbox's running time is recorded — see
    /// <see cref="MeteredSandboxProvider"/> for why there rather than at each place a sandbox stops.
    /// </summary>
    private static ISandboxProvider Metered(IServiceProvider services, ISandboxProvider provider) =>
        new MeteredSandboxProvider(
            provider,
            services.GetRequiredService<IUsageRecorder>(),
            services.GetRequiredService<IOptions<SandboxOptions>>());

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

    /// <summary>
    /// A refusal with something to read. Left to the default it was an empty 429: every screen fell back to its
    /// generic sentence — a publish refused for being the twenty-first in an hour said "That could not be saved." —
    /// and a visitor posting a customer's contact form once too often was shown a blank page. The form endpoint gets
    /// a page, because a browser posted to it; everything else gets a sentence naming what was done too often, and
    /// when to try again, which is also sent as <c>Retry-After</c>.
    /// </summary>
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : (TimeSpan?)null;

        if (wait is { } seconds)
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(seconds.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var minutes = wait is { } span ? Math.Max(1, (int)Math.Ceiling(span.TotalMinutes)) : (int?)null;
        var when = minutes switch { null => "in a few minutes", 1 => "in a minute", var m => $"in {m} minutes" };

        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        if (policy == RateLimitPolicies.Forms)
        {
            await VisitorPage.WriteAsync(
                http.Response,
                StatusCodes.Status429TooManyRequests,
                "Message not sent",
                "Too many messages from here in a short time.",
                $"Your message was not sent. Please wait, then press back and send it again {when}.");

            return;
        }

        var title = policy switch
        {
            RateLimitPolicies.Deploy => "This account has published a lot in the last hour.",
            RateLimitPolicies.Mail => "A lot of email has been asked for from here recently.",
            RateLimitPolicies.SignUp => "A lot of accounts have been created from here recently.",
            RateLimitPolicies.SignIn => "There have been a lot of sign-in attempts from here.",
            _ => "That has been done a lot in a short time."
        };

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        await http.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = title, Detail = $"Please try again {when}." },
            cancellationToken);
    }
}
