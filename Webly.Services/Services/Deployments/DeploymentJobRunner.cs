using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Webly.Data;
using Webly.Data.Models.Deployments;
using Webly.Data.Repositories.Deployments;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services.Email;
using Webly.Services.Services.Email.Templates;
using Webly.Services.Services.Realtime;
using Webly.Services.Services.Rendering;
using Webly.Services.UseCases.Sites;

namespace Webly.Services.Services.Deployments;

/// <summary>
/// Runs queued deployments: render, upload, and — only on success — move the site's published pointer.
///
/// A polling loop over a status column, not a lease table. Webly runs as one instance, and the honest version of that
/// is a runner that says so: a second instance would take the same rows twice, and the fix is a lease column plus a
/// heartbeat (the reference project has both, and needed them because its jobs run for twenty minutes). The cost of
/// skipping that today is written down here rather than discovered later.
///
/// Status is published on the run stream <b>and</b> written to the row. The stream is for the person watching; the row
/// is for the one who closed the tab, which is the case that makes a deployment a job at all.
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

                // One at a time. A deploy is seconds of network and no CPU, and a queue of two is already unusual for
                // one account's worth of publishing.
                foreach (var queued in await repository.ListQueuedAsync(1, stoppingToken))
                    await RunAsync(queued.Nanoid, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // The loop must not die: a runner that stopped polling is a product where publishing silently stopped
                // working, with every row still reading "Queued".
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
        var renderer = serviceProvider.GetRequiredService<ISiteRenderer>();
        var target = serviceProvider.GetRequiredService<IDeploymentTarget>();
        var domains = serviceProvider.GetRequiredService<IDomainRepository>();
        var sites = serviceProvider.GetRequiredService<SiteMapper>();
        var users = serviceProvider.GetRequiredService<IUserRepository>();
        var email = serviceProvider.GetRequiredService<IEmailSender>();
        var sink = serviceProvider.GetRequiredService<IRunEventSink>();

        var deployment = await repository.FindWithSiteAsync(deploymentNanoid, stoppingToken);

        if (deployment is null || deployment.Status is not DeploymentStatus.Queued) return;

        // The run id is the deployment's nanoid, not a fresh one: a deploy is durable, so the thing a client
        // resubscribes to has to be findable after a restart — and the row is the only thing that survives one.
        var handle = registry.Register(RunKind.Deploy, deployment.Nanoid, deployment.Nanoid, deployment.TriggeredByUserId, stoppingToken);

        var site = deployment.Site;
        var version = deployment.SiteVersion;

        try
        {
            await Progress(sink, deployment, DeploymentStatus.Rendering, dbContext, stoppingToken);

            var primary = await domains.FindPrimaryAsync(site.Id, stoppingToken);
            var rendered = renderer.Render(version.Document, new RenderContext(site.Name, sites.UrlFor(site, primary)));

            await Progress(sink, deployment, DeploymentStatus.Uploading, dbContext, stoppingToken);

            site.ProviderProjectId ??= await target.EnsureProjectAsync(site.Nanoid, site.Name, stoppingToken);
            var handleResult = await target.DeployAsync(site.ProviderProjectId, rendered, stoppingToken);

            deployment.ProviderDeploymentId = handleResult.ProviderDeploymentId;
            deployment.ProviderUrl = handleResult.ProviderUrl;
            deployment.Status = DeploymentStatus.Ready;
            deployment.FinishedAt = DateTime.UtcNow;

            // The only place this pointer moves. A failed publish leaves the previous version live, which is the
            // difference between a bad afternoon and a customer's site going down.
            site.PublishedVersionId = version.Id;

            await dbContext.SaveChangesAsync(stoppingToken);

            await sink.EmitAsync(deployment.Nanoid, new RunEvent
            {
                Type = RunEventType.Completed,
                Detail = sites.UrlFor(site, primary)
            }, isTerminal: true, stoppingToken);

            await NotifyAsync(email, users, deployment, site.Name, sites.UrlFor(site, primary), null, stoppingToken);
        }
        catch (Exception exception)
        {
            var message = exception is DeploymentFailedException failure
                ? failure.Message
                : "Publishing failed unexpectedly. Your live site is unchanged.";

            logger.LogError(exception, "Deployment {Deployment} of site {Site} failed.", deployment.Nanoid, site.Nanoid);

            deployment.Status = DeploymentStatus.Failed;
            deployment.Error = message;
            deployment.FinishedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);

            await sink.EmitAsync(deployment.Nanoid, new RunEvent
            {
                Type = RunEventType.Failed,
                Error = message
            }, isTerminal: true, CancellationToken.None);

            await NotifyAsync(email, users, deployment, site.Name, null, message, CancellationToken.None);
        }
        finally
        {
            registry.Finish(handle.RunId);
        }
    }

    private static async Task Progress(
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
    /// Mails the outcome. A deploy is the one thing in Webly long enough for somebody to walk away from, and the whole
    /// point of it is an outside effect — so the result has to reach them somewhere other than a stream they may not
    /// be watching. Failures are mailed too, saying plainly that the live site is untouched.
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
