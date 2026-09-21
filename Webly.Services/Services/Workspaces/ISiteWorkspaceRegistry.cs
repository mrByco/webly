using Webly.Data.Models.Sites;
using Webly.Services.Services.Workspaces;

namespace Webly.Services.Services.Workspaces;

/// <summary>A workspace held for the length of one piece of work. Disposing releases the turn's lock.</summary>
public interface IWorkspaceLease : IAsyncDisposable
{
    SiteWorkspace Workspace { get; }
}

/// <summary>
/// The live workspaces, one per site. In-process and a singleton, for the same reason <c>RunRegistry</c> is:
/// the sandbox belongs to the instance that started it, and a second instance could not reach into it.
/// Scaling out means moving this to shared state and routing a site's turns to the instance holding its
/// workspace — written down here rather than discovered later.
/// </summary>
public interface ISiteWorkspaceRegistry
{
    /// <summary>
    /// The site's workspace, started and seeded if there is none, re-seeded if the site's head has moved,
    /// with the turn's lock held until the lease is disposed.
    ///
    /// <paramref name="onProgress"/> reports the slow parts — starting a machine, installing dependencies,
    /// waiting for a dev server — because a cold workspace is the one moment this product makes somebody wait,
    /// and silence there reads as failure.
    /// </summary>
    Task<IWorkspaceLease> AcquireAsync(
        Site site,
        Func<string, Task>? onProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>The workspace for a site if one is live, without starting one. What the preview proxy asks.</summary>
    SiteWorkspace? Find(string siteNanoid);

    IReadOnlyList<SiteWorkspace> All { get; }

    /// <summary>
    /// Puts a warm workspace on a new commit, or does nothing if the site has none. What a restore calls.
    ///
    /// The alternative is to release the workspace, which is what a restore used to do — and the person who just
    /// pressed "bring this back" then watched their preview become "your preview is not running, send a message to
    /// wake it up". It also threw away a warm sandbox that costs about twelve seconds to replace. Re-seeding is the
    /// same work the lease already does when it finds the head has moved, so this is that path, asked for directly.
    /// </summary>
    Task ReseedAsync(Site site, string headSha, CancellationToken cancellationToken = default);

    /// <summary>Stops a workspace and forgets it. Called by the reaper, and when a site is deleted.</summary>
    Task ReleaseAsync(string siteNanoid);
}
