using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Webly.Data.Models.Sites;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Services.Workspaces;

public class SiteWorkspaceRegistry(
    ISandboxProvider sandboxes,
    ISiteRepositoryStore repositories,
    IOptions<SandboxOptions> options,
    IOptions<AppOptions> app,
    ILogger<SiteWorkspaceRegistry> logger) : ISiteWorkspaceRegistry
{
    private readonly ConcurrentDictionary<string, SiteWorkspace> _workspaces = new();

    /// <summary>
    /// One at a time per site through "find a workspace or start one".
    ///
    /// The dictionary cannot do this on its own: starting a sandbox is several seconds of awaiting, so
    /// "not in the dictionary" and "put it in the dictionary" are separated by long enough for a second turn to
    /// read the same absence. Both then started their own sandbox and the second overwrote the first in the
    /// dictionary — so the two turns never met the gate that is supposed to queue them, each committed from its
    /// own copy of the same tree, and the loser was refused. One paid machine was also left running with
    /// nothing pointing at it. It only happens on a <b>cold</b> site, which is why the warm case has always
    /// looked right.
    ///
    /// A lock per site rather than one for the registry: two people's sites starting at the same moment is the
    /// ordinary case and has nothing to serialize. Entries are deliberately never removed — a semaphore is a few
    /// bytes and one per site this process has ever opened is the same order as anything else keyed by site,
    /// while removing one somebody is queued on would hand the next caller a fresh lock and reopen exactly the
    /// race this closes.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _starting = new();

    private readonly SandboxOptions _options = options.Value;

    public IReadOnlyList<SiteWorkspace> All => [.. _workspaces.Values];

    public SiteWorkspace? Find(string siteNanoid) =>
        _workspaces.TryGetValue(siteNanoid, out var workspace) ? workspace : null;

    public async Task<bool> IsPreviewReadyAsync(string siteNanoid, CancellationToken cancellationToken = default)
    {
        if (!_workspaces.TryGetValue(siteNanoid, out var workspace) || !workspace.DevServerStarted) return false;

        var health = await workspace.Sandbox.ReadHealthAsync(cancellationToken);

        return health is { Reachable: true, DevServerRunning: true };
    }

    public async Task<IWorkspaceLease> AcquireAsync(
        Site site,
        Func<string, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        // From git rather than from the entity, and the order matters. `site` is a snapshot taken before this
        // call — possibly before another turn on the same site committed — and seeding from a stale snapshot
        // writes the tree that turn just replaced back into the sandbox, so the preview shows somebody's change
        // being undone. The branch is what a commit actually moves, so the branch is what to ask.
        var headSha = await repositories.ResolveHeadAsync(site.Nanoid, site.DefaultBranch, cancellationToken)
            ?? site.HeadVersion?.CommitSha
            ?? throw new SandboxException("This site has no commits yet.");

        var starting = _starting.GetOrAdd(site.Nanoid, _ => new SemaphoreSlim(1, 1));
        SiteWorkspace workspace;

        await starting.WaitAsync(cancellationToken);

        try
        {
            workspace = await FindOrStartAsync(site, headSha, onProgress, cancellationToken);
        }
        finally
        {
            starting.Release();
        }

        // Only one turn at a time on a workspace; see SiteWorkspace.Gate.
        await workspace.Gate.WaitAsync(cancellationToken);

        try
        {
            // Asked again, now that the lease is held, and that is not belt and braces. The resolve above
            // happens before the queue: a turn that waited behind another one on the same site read the branch
            // as it was *before* the winner committed, so re-seeding to it would copy the tree the winner
            // replaced back into the sandbox — and the loser's commit would then revert work somebody watched
            // land. Which is the defect one layer down, in the same shape: a stale sha used as if it were
            // current. Inside the gate there is nothing left to race with, because the gate is what a commit is
            // made behind.
            headSha = await repositories.ResolveHeadAsync(site.Nanoid, site.DefaultBranch, cancellationToken)
                ?? headSha;

            // The head moved while this workspace was warm — a restore, a hand edit, or the turn that just
            // finished ahead of this one. The sandbox's tree is the old commit, so re-seed it: committing on top
            // of the current head from a stale tree would silently revert whatever moved it.
            if (workspace.CommitSha != headSha)
            {
                logger.LogInformation(
                    "Re-seeding {Site} from {Old} to {New}.", site.Nanoid, workspace.CommitSha, headSha);

                if (onProgress is not null) await onProgress("Catching up with your latest changes");

                await SeedAsync(workspace.Sandbox, site, headSha, cancellationToken);

                workspace.CommitSha = headSha;

                // The agent's session reasoned about the files that were there before. Resuming it now would
                // have it editing a tree it has not seen.
                workspace.AgentSessionId = null;
                workspace.AgentKey = null;
            }

            await EnsureDevServerAsync(workspace, site, onProgress, cancellationToken);

            workspace.Touch();

            return new Lease(workspace);
        }
        catch
        {
            workspace.Gate.Release();
            throw;
        }
    }

    /// <summary>
    /// The site's warm workspace, starting one if there is none. Called only under <see cref="_starting"/>, which
    /// is what makes the check and the act one step — see that field for what happened when they were two.
    /// </summary>
    private async Task<SiteWorkspace> FindOrStartAsync(
        Site site,
        string headSha,
        Func<string, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        var workspace = _workspaces.TryGetValue(site.Nanoid, out var existing) ? existing : null;

        // A workspace whose sandbox has died — the provider's own timeout, a crash, a deploy — is worse than
        // none: every command would fail with a transport error. Checked before it is used rather than trusted
        // because it is in a dictionary.
        if (workspace is not null && !await workspace.Sandbox.IsHealthyAsync(cancellationToken))
        {
            logger.LogInformation("Workspace for {Site} is unreachable; starting a new one.", site.Nanoid);
            await ReleaseAsync(site.Nanoid);
            workspace = null;
        }

        if (workspace is not null) return workspace;

        if (onProgress is not null) await onProgress("Waking up your site");

        workspace = await StartAsync(site, headSha, onProgress, cancellationToken);
        _workspaces[site.Nanoid] = workspace;

        return workspace;
    }

    public async Task ReseedAsync(Site site, string headSha, CancellationToken cancellationToken = default)
    {
        if (!_workspaces.TryGetValue(site.Nanoid, out var workspace)) return;

        // Behind the same gate a turn takes, so this cannot write a tree under an agent that is mid-edit: a restore
        // arriving during a turn waits for it, and the turn's own commit then leaves the head where this put it.
        await workspace.Gate.WaitAsync(cancellationToken);

        try
        {
            await SeedAsync(workspace.Sandbox, site, headSha, cancellationToken);

            workspace.CommitSha = headSha;

            // For the reason AcquireAsync does it: a resumed session would be reasoning about files that are gone.
            workspace.AgentSessionId = null;
            workspace.AgentKey = null;

            // The dev server is still running and watching the files, so it recompiles by itself and the preview
            // shows the restored site without anybody asking — which is the point of not releasing the workspace.
            await EnsureDevServerAsync(workspace, site, onProgress: null, cancellationToken);

            workspace.Touch();
        }
        finally
        {
            workspace.Gate.Release();
        }
    }

    /// <summary>
    /// Starts the dev server again if it has stopped.
    ///
    /// The sandbox being healthy does not mean the site is being served: the dev server is a child process inside
    /// it, and it can die on its own — the out-of-memory killer takes it first on a machine running several, and a
    /// crash is a crash. Nothing noticed. The workspace stayed in the dictionary, `workspaceReady` stayed true, and
    /// the preview answered 502 for the rest of the session while every turn reported success; the build check read
    /// an empty log and concluded the site compiled, because nothing was compiling.
    ///
    /// One request per turn, which is the cheapest place to ask: a turn is already seconds long, and it is also the
    /// moment somebody is watching the preview for a change.
    /// </summary>
    private async Task EnsureDevServerAsync(
        SiteWorkspace workspace,
        Site site,
        Func<string, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        if (!workspace.DevServerStarted) return;

        var log = await workspace.Sandbox.ReadDevServerLogAsync(cancellationToken: cancellationToken);

        if (log.Running) return;

        logger.LogWarning(
            "The dev server for {Site} had stopped; starting it again. Its last words: {Tail}",
            site.Nanoid,
            log.Text.Length <= 400 ? log.Text : log.Text[^400..]);

        if (onProgress is not null) await onProgress("Starting the preview again");

        await workspace.Sandbox.StartDevServerAsync(
            $"/api/sites/{site.Nanoid}/preview", DevServerEnvironment(site), cancellationToken);
    }

    /// <summary>
    /// What the site's own code is told while it runs in the editor. One entry, and it is the same value the
    /// publish passes: the preview's forms have to post to the same place the published site's do, or a form
    /// somebody tested in the editor is not the form their visitors use.
    /// </summary>
    private Dictionary<string, string> DevServerEnvironment(Site site) => new()
    {
        ["NEXT_PUBLIC_FORM_ENDPOINT"] = app.Value.FormEndpointFor(site.Nanoid)
    };

    private async Task<SiteWorkspace> StartAsync(
        Site site,
        string headSha,
        Func<string, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        // No secrets in the sandbox's base environment: the agent's key is passed per command, for the one
        // command that needs it, so a shell the agent opens for its own reasons does not inherit it.
        var sandbox = await sandboxes.StartAsync(
            new SandboxSpec(site.Nanoid, new Dictionary<string, string>()), cancellationToken);

        try
        {
            await SeedAsync(sandbox, site, headSha, cancellationToken);

            if (onProgress is not null) await onProgress("Starting the preview");

            // The path a browser reaches this dev server at. Built here rather than in the sandbox because the
            // sandbox has no idea what Webly's routes look like, and it is one string away from PreviewController's.
            await sandbox.StartDevServerAsync(
                $"/api/sites/{site.Nanoid}/preview", DevServerEnvironment(site), cancellationToken);

            return new SiteWorkspace(site.Nanoid, sandbox, headSha) { DevServerStarted = true };
        }
        catch
        {
            await sandbox.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Puts a commit's tree in the sandbox and makes sure the dependencies match it.
    ///
    /// An install runs only when the tree's dependencies are not already there, which is what makes a cold
    /// workspace fast: the sandbox image has the starter template's dependencies installed, so the common case
    /// is a tree copy and nothing else. A site whose agent added a package pays the install once.
    ///
    /// <b><c>npm ci</c> first, and that is not a preference.</b> <c>npm install</c> resolves the tree afresh and
    /// writes <c>package-lock.json</c> back — so seeding a workspace edited the site's source, and the turn
    /// committed the result as the person's change. The first real turn through this code produced a version
    /// whose diff was the headline they asked for <i>and eighty-four deleted lines of lockfile</i>. It is worse
    /// than noise in a history: the publish path runs <c>npm ci</c> against that lockfile, so a file nobody
    /// reviewed decides what the built site is made of. <c>npm ci</c> installs what the lockfile says and never
    /// writes it.
    ///
    /// The fallback is the one case <c>npm ci</c> refuses: a <c>package.json</c> the lockfile does not match,
    /// which is what an agent that edited dependencies by hand leaves behind. Then resolving really is the job,
    /// and the lockfile it writes is an honest part of that turn's change.
    /// </summary>
    private async Task SeedAsync(
        ISandbox sandbox,
        Site site,
        string commitSha,
        CancellationToken cancellationToken)
    {
        var tree = await repositories.ReadTreeAsync(site.Nanoid, commitSha, cancellationToken);

        await sandbox.WriteTreeAsync(tree, cancellationToken);

        var install = await sandbox.RunAsync(
            new SandboxCommand("sh", ["-c", "test -d node_modules && npm ls --depth=0 >/dev/null 2>&1"]),
            cancellationToken: cancellationToken);

        if (install.Succeeded) return;

        logger.LogInformation("Installing dependencies for {Site}.", site.Nanoid);

        var clean = await sandbox.RunAsync(
            new SandboxCommand("npm", ["ci", "--no-audit", "--no-fund"], TimeSpan.FromMinutes(5)),
            cancellationToken: cancellationToken);

        if (clean.Succeeded) return;

        var resolved = await sandbox.RunAsync(
            new SandboxCommand("npm", ["install", "--no-audit", "--no-fund"], TimeSpan.FromMinutes(5)),
            cancellationToken: cancellationToken);

        if (!resolved.Succeeded)
            throw new SandboxException(
                "The site's dependencies could not be installed.",
                $"npm ci:\n{clean.Output}\n\nnpm install:\n{resolved.Output}");
    }

    public async Task ReleaseAsync(string siteNanoid)
    {
        if (!_workspaces.TryRemove(siteNanoid, out var workspace)) return;

        // Removed from the dictionary first, so nothing new can lease it, then waited for: stopping a sandbox
        // out from under a running turn would fail the turn with a transport error instead of an explanation.
        // The wait is bounded, because a wedged turn must not keep a paid sandbox for ever.
        if (await workspace.Gate.WaitAsync(TimeSpan.FromSeconds(30)))
            workspace.Gate.Release();

        await workspace.Sandbox.DisposeAsync();

        logger.LogInformation("Released the workspace for {Site}.", siteNanoid);
    }

    public async Task ReleaseAllAsync()
    {
        // Drained before anything is stopped, so a turn that is still running cannot lease one of these back
        // while this is working through them.
        var open = _workspaces.Keys.ToList().Select(key => _workspaces.TryRemove(key, out var workspace) ? workspace : null)
            .OfType<SiteWorkspace>()
            .ToList();

        if (open.Count == 0) return;

        logger.LogInformation("Stopping {Count} warm workspace(s) before shutting down.", open.Count);

        // In parallel, and not waiting on the gate: see the interface. Stopping a local sandbox means killing a
        // process tree and waiting for it, so doing eight of them in a row is how a shutdown overruns.
        await Task.WhenAll(open.Select(async workspace =>
        {
            try
            {
                await workspace.Sandbox.DisposeAsync();
            }
            catch (Exception exception)
            {
                // One sandbox that will not stop must not keep the others alive — and on the way out of the
                // process there is nothing left to retry with.
                logger.LogWarning(exception, "Could not stop the workspace for {Site}.", workspace.SiteNanoid);
            }
        }));
    }

    private sealed class Lease(SiteWorkspace workspace) : IWorkspaceLease
    {
        public SiteWorkspace Workspace => workspace;

        public ValueTask DisposeAsync()
        {
            workspace.Touch();
            workspace.Gate.Release();

            return ValueTask.CompletedTask;
        }
    }
}
