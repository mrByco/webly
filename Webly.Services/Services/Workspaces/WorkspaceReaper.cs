using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Services.Workspaces;

/// <summary>
/// Stops workspaces nobody is using, and every workspace when the process stops.
///
/// This is the one background service with a direct line to the bill: a warm sandbox costs money per second
/// whether or not anybody is typing, so the idle timeout is not tidiness, it is the product's unit economics.
/// The lifetime cap beside it is for the case the idle clock cannot see — a wedged turn that keeps touching
/// its workspace, or a dev server in a rebuild loop.
///
/// Shutdown belongs to the same argument, and nothing owned it: **stopping the app left one sandbox running
/// per open site**. Locally that is a node process each, which is how fourteen of them were found still
/// listening five hours after the backend that started them had been restarted six times — idle, holding
/// nine hundred megabytes between them, with their workspaces already deleted from under them by the next
/// start's sweep. On a provider that bills by the second it is a sandbox per site, per deploy, for ever, and
/// nothing in this codebase would have said so. `LocalSandboxProvider`'s sweep is the other half and covers
/// only the case this cannot — a process that was killed rather than asked to stop.
/// </summary>
public class WorkspaceReaper(
    ISiteWorkspaceRegistry workspaces,
    IOptions<SandboxOptions> options,
    ILogger<WorkspaceReaper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        var settings = options.Value;

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;

            foreach (var workspace in workspaces.All)
            {
                var idle = now - workspace.LastUsedAt;
                var age = now - workspace.StartedAt;

                if (idle <= settings.IdleTimeout && age <= settings.MaxLifetime) continue;

                logger.LogInformation(
                    "Stopping the workspace for {Site}: idle {Idle}, age {Age}.", workspace.SiteNanoid, idle, age);

                try
                {
                    await workspaces.ReleaseAsync(workspace.SiteNanoid);
                }
                catch (Exception exception)
                {
                    // Never let one stuck sandbox stop the sweep: the next one on the list may be the expensive one.
                    logger.LogError(exception, "Could not stop the workspace for {Site}.", workspace.SiteNanoid);
                }
            }
        }
    }

    /// <summary>
    /// Stops every warm workspace on the way out.
    ///
    /// Before the base call, deliberately: <see cref="BackgroundService.StopAsync"/> cancels the token the loop
    /// above is waiting on and then awaits it, and this has nothing to do with that loop — it is the work that
    /// has to happen whether the loop was mid-tick or asleep.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await workspaces.ReleaseAllAsync();
        }
        catch (Exception exception)
        {
            // A shutdown that throws here would hide whatever else the host is trying to stop, and the sweep at
            // the next start is the backstop for exactly this.
            logger.LogError(exception, "Could not stop the warm workspaces while shutting down.");
        }

        await base.StopAsync(cancellationToken);
    }
}
