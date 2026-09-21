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
        var headSha = site.HeadVersion?.CommitSha
            ?? await repositories.ResolveHeadAsync(site.Nanoid, site.DefaultBranch, cancellationToken)
            ?? throw new SandboxException("This site has no commits yet.");

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

        if (workspace is null)
        {
            if (onProgress is not null) await onProgress("Waking up your site");

            workspace = await StartAsync(site, headSha, onProgress, cancellationToken);
            _workspaces[site.Nanoid] = workspace;
        }

        // Only one turn at a time on a workspace; see SiteWorkspace.Gate.
        await workspace.Gate.WaitAsync(cancellationToken);

        try
        {
            // The head moved while this workspace was warm — a restore, or a hand edit. The sandbox's tree is
            // the old commit, so re-seed it: committing on top of the current head from a stale tree would
            // silently revert whatever moved it.
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
