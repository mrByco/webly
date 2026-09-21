using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Workspaces;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Starts a site's workspace because somebody asked to look at their site, rather than because they changed it.
///
/// It exists because the only way to see a preview used to be to send a message — which starts an agent turn, costs
/// a model call and writes a version. "Looking" and "changing" had one button between them, and the cold preview's
/// own sentence said so: <i>send a message and it wakes up</i>. Publishing something and then wanting to see it is
/// not a reason to edit it.
///
/// <b>It answers before the workspace is ready</b>, and that is the shape rather than a shortcut: starting one is
/// tens of seconds, which is far too long to hold an HTTP request open and long enough that a person needs to see
/// something happening. The work runs on its own DI scope so it outlives this request, and the client watches
/// <see cref="SiteDetailResponse.WorkspaceReady"/> — the flag the editor already reads on load.
/// </summary>
public class WakeSiteWorkspace(
    ISiteRepository siteRepository,
    ISiteWorkspaceRegistry workspaces,
    IServiceScopeFactory scopeFactory,
    ILogger<WakeSiteWorkspace> logger)
{
    public async Task<Result<SiteError, WakeWorkspaceResponse>> ExecuteAsync(
        int userId,
        string nanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerAsync(nanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError, WakeWorkspaceResponse>.Fail(SiteError.NotFound);

        // Asked of the sandbox, not of the registry: a workspace whose machine has gone away or whose dev server
        // died is in the dictionary and cannot show anybody a preview, and answering "already running" to that is
        // how the pane ends up as an iframe over a 502. The lease below repairs both cases.
        if (await workspaces.IsPreviewReadyAsync(site.Nanoid, cancellationToken))
            return Result<SiteError, WakeWorkspaceResponse>.Ok(new WakeWorkspaceResponse { AlreadyRunning = true });

        // Fire and forget, deliberately: nothing waits on this and nothing should. A second request while it is
        // starting finds the workspace in the registry and answers `alreadyRunning`, and one that arrives a moment
        // too early is harmless — the registry's own lock makes a second start wait for the first.
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();

            var sites = scope.ServiceProvider.GetRequiredService<ISiteRepository>();
            var registry = scope.ServiceProvider.GetRequiredService<ISiteWorkspaceRegistry>();

            try
            {
                // Re-read on the new scope: the site entity above belongs to the request's DbContext, which is
                // disposed the moment this method returns.
                var fresh = await sites.FindForOwnerAsync(nanoid, userId, CancellationToken.None);

                if (fresh is null) return;

                await using var lease = await registry.AcquireAsync(fresh, cancellationToken: CancellationToken.None);
            }
            catch (Exception exception)
            {
                // Logged and dropped. The client is watching `workspaceReady`, which simply stays false — and the
                // next thing it does, a message, reports the same failure where somebody is waiting for an answer.
                logger.LogWarning(exception, "Waking the workspace for site {Site} failed.", nanoid);
            }
        }, CancellationToken.None);

        return Result<SiteError, WakeWorkspaceResponse>.Ok(new WakeWorkspaceResponse { AlreadyRunning = false });
    }
}
