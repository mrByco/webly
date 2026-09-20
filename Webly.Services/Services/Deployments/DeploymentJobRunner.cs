using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Webly.Data;
using Webly.Data.Models.Deployments;
using Webly.Data.Repositories.Deployments;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Realtime;
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

            var deployed = await target.BuildAndDeployAsync(
                sandbox,
                site.ProviderProjectId,
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

            var primary = await domains.FindPrimaryAsync(site.Id, stoppingToken);
            var url = mapper.UrlFor(site, primary);

            await sink.EmitAsync(deployment.Nanoid, new RunEvent
            {
                Type = RunEventType.Completed,
                Detail = url
            }, isTerminal: true, stoppingToken);

            await NotifyAsync(email, users, deployment, site.Name, url, null, stoppingToken);
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

            var detail = exception switch
            {
                DeploymentFailedException failure => failure.ProviderDetail,
                SandboxException sandboxFailure => sandboxFailure.Detail,
                _ => exception.Message
            };

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

            await NotifyAsync(email, users, deployment, site.Name, null, message, CancellationToken.None);
        }
        finally
        {
            // Always: a build sandbox left running is the most expensive mistake in this file.
            if (sandbox is not null) await sandbox.DisposeAsync();

            registry.Finish(handle.RunId);
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
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await users.FindByIdAsync(deployment.TriggeredByUserId, cancellationToken);

            if (user is null) return;

            var message = error is null
                ? WeblyEmails.SitePublished(user.Email, user.DisplayName, siteName, url!)
                : WeblyEmails.DeploymentFailed(user.Email, user.DisplayName, siteName, error);

            await email.SendAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            // A mail that did not go out must not turn a successful publish into a failed one.
            logger.LogWarning(exception, "Could not mail the outcome of deployment {Deployment}.", deployment.Nanoid);
        }
    }
}
