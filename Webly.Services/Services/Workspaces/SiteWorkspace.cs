using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Services.Workspaces;

/// <summary>
/// One site's live editing session: a sandbox, the commit it was seeded from, and a dev server.
///
/// It exists because both of the product's central experiences need the same warm machine. An agent turn
/// wants the workspace it edited last time (its own session, its installed dependencies, its running dev
/// server); the preview wants a dev server that is already compiled. Starting a sandbox per turn would make
/// every request cost a cold start, and running two would mean the preview showed a different tree from the
/// one being edited.
///
/// <b>One workspace per site, not per person or per run.</b> A site has one owner and one branch, so a second
/// workspace could only be a second tree diverging from the same commit — which is a merge conflict the
/// product has no way to talk about. <see cref="Gate"/> is what serializes turns on it.
/// </summary>
public sealed class SiteWorkspace(string siteNanoid, ISandbox sandbox, string commitSha)
{
    public string SiteNanoid => siteNanoid;
    public ISandbox Sandbox => sandbox;

    /// <summary>
    /// The commit this workspace was seeded from. The next commit's parent, and the check that decides whether
    /// a workspace can be reused: if the site's head has moved since (a restore, a hand edit), the tree in the
    /// sandbox is stale and has to be re-seeded before anything else edits it.
    /// </summary>
    public string CommitSha { get; internal set; } = commitSha;

    /// <summary>
    /// The coding agent's own session in this workspace, so the next turn resumes rather than re-reads the
    /// codebase. Cleared when the tree is re-seeded, because a resumed session would then be reasoning about
    /// files that have changed underneath it.
    /// </summary>
    public string? AgentSessionId { get; internal set; }

    /// <summary>Which agent that session belongs to — resuming it with the other one would be nonsense.</summary>
    public string? AgentKey { get; internal set; }

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public DateTime LastUsedAt { get; internal set; } = DateTime.UtcNow;

    public bool DevServerStarted { get; internal set; }

    /// <summary>
    /// Serializes work on the workspace. Two turns in one sandbox would interleave file writes and produce a
    /// commit neither of them asked for; the preview reading a half-written tree is the same problem more
    /// quietly. A second turn waits.
    /// </summary>
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    internal void Touch() => LastUsedAt = DateTime.UtcNow;
}
