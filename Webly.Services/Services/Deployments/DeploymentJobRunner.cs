using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Webly.Data;
using Webly.Data.Models.Deployments;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Deployments;
using Webly.Services.Agent;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services;
using Webly.Services.Services.Email;
using Webly.Services.Services.Email.Templates;
using Webly.Services.Services.Realtime;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sandboxes;
using Webly.Services.UseCases.Sites;

namespace Webly.Services.Services.Deployments;

/// <summary>
/// Runs queued deployments: a clean sandbox, the exact commit, install, build, upload — and only on success does
/// the site's published pointer move.
///
/// <b>A fresh sandbox, not the editing workspace.</b> The workspace is mid-edit by definition: it may hold
/// uncommitted files, a dev server, an agent's half-finished thought. Publishing from it would mean the thing
/// that goes live is not the thing the history names. So a deploy checks out its own commit and pays for a cold
/// start, which is the right trade for the one operation that is visible to the public.
///
/// A polling loop over a status column, not a lease table. Webly runs as one instance, and the honest version of
/// that is a runner that says so: a second instance would take the same row twice, and the fix is a lease column
/// plus a heartbeat. The cost of skipping that today is written down here rather than discovered later.
/// </summary>
public class DeploymentJobRunner(
    IServiceScopeFactory scopeFactory,
    RunRegistry registry,
    ILogger<DeploymentJobRunner> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IDeploymentRepository>();

                // One at a time. A build is minutes of one sandbox, and a queue of two is already unusual for one
                // account's worth of publishing.
                foreach (var queued in await repository.ListQueuedAsync(1, stoppingToken))
                    await RunAsync(queued.Nanoid, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // The loop must not die: a runner that stopped polling is a product where publishing silently
                // stopped working, with every row still reading "Queued".
                logger.LogError(exception, "The deployment runner's poll failed; it will try again.");
            }
        }
    }

    private async Task RunAsync(string deploymentNanoid, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        var repository = serviceProvider.GetRequiredService<IDeploymentRepository>();
        var dbContext = serviceProvider.GetRequiredService<WeblyDbContext>();
        var repositories = serviceProvider.GetRequiredService<ISiteRepositoryStore>();
        var sandboxes = serviceProvider.GetRequiredService<ISandboxProvider>();
        var target = serviceProvider.GetRequiredService<IDeploymentTarget>();
        var domains = serviceProvider.GetRequiredService<IDomainRepository>();
        var mapper = serviceProvider.GetRequiredService<SiteMapper>();
        var users = serviceProvider.GetRequiredService<IUserRepository>();
        var email = serviceProvider.GetRequiredService<IEmailSender>();
        var sink = serviceProvider.GetRequiredService<IRunEventSink>();
        var sites = serviceProvider.GetRequiredService<IOptions<SitesOptions>>();
        var app = serviceProvider.GetRequiredService<IOptions<AppOptions>>();

        var deployment = await repository.FindWithSiteAsync(deploymentNanoid, stoppingToken);

        if (deployment is null || deployment.Status is not DeploymentStatus.Queued) return;

        // The run id is the deployment's nanoid, not a fresh one: a deploy is durable, so the thing a client
        // resubscribes to has to be findable after a restart — and the row is the only thing that survives one.
        var handle = registry.Register(
            RunKind.Deploy, deployment.Nanoid, deployment.Nanoid, deployment.TriggeredByUserId, stoppingToken);

        var site = deployment.Site;
        var version = deployment.SiteVersion;
        ISandbox? sandbox = null;

        try
        {
            await ProgressAsync(sink, deployment, DeploymentStatus.Preparing, dbContext, stoppingToken);

            sandbox = await sandboxes.StartAsync(
                new SandboxSpec(site.Nanoid, new Dictionary<string, string>()), stoppingToken);

            var tree = await repositories.ReadTreeAsync(site.Nanoid, version.CommitSha, stoppingToken);
            await sandbox.WriteTreeAsync(tree, stoppingToken);

            // `npm ci`, not `npm install`: the lockfile is committed, and a deploy that resolved a different
            // dependency tree from the one the agent tested against is the class of bug nobody can reproduce.
            var install = await sandbox.RunAsync(
                new SandboxCommand("npm", ["ci", "--no-audit", "--no-fund"], TimeSpan.FromMinutes(5)),
                cancellationToken: stoppingToken);

            if (!install.Succeeded)
                throw new DeploymentFailedException(
                    "Your site's dependencies could not be installed, so nothing was published.",
                    install.Output);

            await ProgressAsync(sink, deployment, DeploymentStatus.Building, dbContext, stoppingToken);

            site.ProviderProjectId ??= await target.EnsureProjectAsync(site.Nanoid, site.Name, stoppingToken);

            // The address, resolved before the build rather than after it: the published pages carry it, so the
            // build has to be told. `UrlFor` and not `LiveUrlFor` — a canonical link that changed with every
            // deployment would tell search engines the site moves every time somebody fixes a headline.
            var primary = await domains.FindPrimaryAsync(site.Id, stoppingToken);
            var address = mapper.UrlFor(site, primary);

            var deployed = await target.BuildAndDeployAsync(
                sandbox,
                site.ProviderProjectId,
                new SiteBuildSettings(address, app.Value.FormEndpointFor(site.Nanoid)),
                // The build log, streamed: a build is the longest wait in the product, and a person watching a
                // progress bar with no output assumes it has hung.
                text => sink.EmitAsync(deployment.Nanoid, new RunEvent
                {
                    Type = RunEventType.DeploymentProgress,
                    Detail = "Building",
                    Text = text
                }, cancellationToken: stoppingToken),
                stoppingToken);

            deployment.ProviderDeploymentId = deployed.ProviderDeploymentId;
            deployment.ProviderUrl = deployed.ProviderUrl;
            deployment.Status = DeploymentStatus.Ready;
            deployment.FinishedAt = DateTime.UtcNow;

            // The only place this pointer moves. A failed publish leaves the previous version live, which is the
            // difference between a bad afternoon and a customer's site going down.
            site.PublishedVersionId = version.Id;

            await dbContext.SaveChangesAsync(stoppingToken);

            // The address the whole product prints, arranged rather than assumed. See EnsureAddressAsync.
            await EnsureAddressAsync(target, site, sites.Value.HostFor(site.Slug), dbContext, stoppingToken);

            // Where it can be opened, which is not always its address — see `SiteMapper`. The email and the
            // terminal event carry the same one the header links, because a link in an email that 404s is worse
            // than no link: it reads as "the publish said it worked and it did not".
            var url = mapper.LiveUrlFor(site, primary, deployment) ?? mapper.UrlFor(site, primary);

            await sink.EmitAsync(deployment.Nanoid, new RunEvent
            {
                Type = RunEventType.Completed,
                Detail = url
            }, isTerminal: true, stoppingToken);

            await NotifyAsync(email, users, deployment, site.Name, url, null, null, stoppingToken);
        }
        catch (Exception exception)
        {
            var message = exception switch
            {
                DeploymentFailedException failure => failure.Message,
                SandboxException => "Publishing could not start because no build machine was available. Please try again.",
                RepositoryException => "That version of your site could not be read. Please try publishing again.",
                _ => "Publishing failed unexpectedly. Your live site is unchanged."
            };

            // Through the same reader the chat's build block uses: this text is shown to the site's owner, and a
            // build log with the terminal's colour codes in it, a Rust backtrace from SWC's internals and the
            // absolute path of a machine they have never seen is not something anybody can act on.
            var detail = Tail(CompilerOutput.Readable(exception switch
            {
                DeploymentFailedException failure => failure.ProviderDetail ?? string.Empty,
                SandboxException sandboxFailure => sandboxFailure.Detail ?? string.Empty,
                _ => exception.Message
            }));

            logger.LogError(exception, "Deployment {Deployment} of site {Site} failed.", deployment.Nanoid, site.Nanoid);

            deployment.Status = DeploymentStatus.Failed;
            // Both, because they answer different questions: the message is for the person, the detail is the
            // build log they need in order to ask the agent to fix it.
            deployment.Error = message;
            deployment.ErrorDetail = detail;
            deployment.FinishedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);

            await sink.EmitAsync(deployment.Nanoid, new RunEvent
            {
                Type = RunEventType.Failed,
                Error = message,
                Detail = detail
            }, isTerminal: true, CancellationToken.None);

            await NotifyAsync(email, users, deployment, site.Name, null, message, detail, CancellationToken.None);
        }
        finally
        {
            // Always: a build sandbox left running is the most expensive mistake in this file.
            if (sandbox is not null) await sandbox.DisposeAsync();

            registry.Finish(handle.RunId);
        }
    }

    /// <summary>The end of a log, which is where a build says what went wrong. Trimmed after it is made
    /// readable, never before: the stripping needs the whole thing to recognise its own shapes.</summary>
    private static string Tail(string output) => output.Length <= 2000 ? output : output[^2000..];

    /// <summary>
    /// Tells the provider to serve the site's Webly subdomain, once, and records it when it takes.
    ///
    /// The subdomain is the whole self-service premise — a published site has an address before anybody has bought
    /// a domain — and a provider serves a hostname only when that hostname has been attached to the project. So it
    /// has to be attached by something, and the first successful publish is the earliest moment there is a project
    /// to attach it to. While it has not taken, every later publish tries again: that is a retry loop with no timer
    /// and no state machine, and the cost of a spare provider call is one call per publish of an unaddressed site.
    ///
    /// <b>Best effort, deliberately.</b> A publish that succeeded must not be reported as failed because a domain
    /// call did: the site is live at the deployment's own URL either way, and that is the URL the person is given
    /// until this works. The development target answers unverified, which is honest — nothing local resolves a
    /// subdomain of the production zone.
    /// </summary>
    private async Task EnsureAddressAsync(
        IDeploymentTarget target,
        Site site,
        string hostname,
        WeblyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (site.AddressReadyAt is not null || site.ProviderProjectId is null) return;

        try
        {
            var attachment = await target.AttachDomainAsync(site.ProviderProjectId, hostname, cancellationToken);

            if (!attachment.Verified)
            {
                logger.LogInformation(
                    "The address {Hostname} for site {Site} is not being served yet ({Error}); the next publish will ask again.",
                    hostname, site.Nanoid, attachment.Error ?? "no error reported");
                return;
            }

            site.AddressReadyAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Site {Site} is now served at {Hostname}.", site.Nanoid, hostname);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Attaching the address {Hostname} to site {Site} failed; its deployment URL is unaffected.",
                hostname, site.Nanoid);
        }
    }

    private static async Task ProgressAsync(
        IRunEventSink sink,
        Deployment deployment,
        DeploymentStatus status,
        WeblyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        deployment.Status = status;
        deployment.StartedAt ??= DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await sink.EmitAsync(deployment.Nanoid, new RunEvent
        {
            Type = RunEventType.DeploymentProgress,
            Detail = status.ToString()
        }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Mails the outcome. A deploy is the one thing in Webly long enough for somebody to walk away from, and the
    /// whole point of it is an outside effect — so the result has to reach them somewhere other than a stream
    /// they may not be watching. Failures are mailed too, saying plainly that the live site is untouched.
    /// </summary>
    private async Task NotifyAsync(
        IEmailSender email,
        IUserRepository users,
        Deployment deployment,
        string siteName,
        string? url,
        string? error,
        string? detail,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await users.FindByIdAsync(deployment.TriggeredByUserId, cancellationToken);

            if (user is null) return;

            var message = error is null
                ? WeblyEmails.SitePublished(user.Email, user.DisplayName, siteName, url!)
                // The build log travels with it. The person reading this is, by definition, not looking at the
                // screen where it is folded away — that is the whole reason the mail exists.
                : WeblyEmails.DeploymentFailed(user.Email, user.DisplayName, siteName, error, detail);

            await email.SendAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            // A mail that did not go out must not turn a successful publish into a failed one.
            logger.LogWarning(exception, "Could not mail the outcome of deployment {Deployment}.", deployment.Nanoid);
        }
    }
}
