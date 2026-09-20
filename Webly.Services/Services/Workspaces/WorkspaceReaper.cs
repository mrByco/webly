using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Services.Workspaces;

/// <summary>
/// Stops workspaces nobody is using.
///
/// This is the one background service with a direct line to the bill: a warm sandbox costs money per second
/// whether or not anybody is typing, so the idle timeout is not tidiness, it is the product's unit economics.
/// The lifetime cap beside it is for the case the idle clock cannot see — a wedged turn that keeps touching
/// its workspace, or a dev server in a rebuild loop.
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
}
