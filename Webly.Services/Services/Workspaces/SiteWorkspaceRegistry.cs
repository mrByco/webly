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
    ILogger<SiteWorkspaceRegistry> logger) : ISiteWorkspaceRegistry
{
    private readonly ConcurrentDictionary<string, SiteWorkspace> _workspaces = new();
    private readonly SandboxOptions _options = options.Value;

    public IReadOnlyList<SiteWorkspace> All => [.. _workspaces.Values];

    public SiteWorkspace? Find(string siteNanoid) =>
        _workspaces.TryGetValue(siteNanoid, out var workspace) ? workspace : null;

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

            workspace.Touch();

            return new Lease(workspace);
        }
        catch
        {
            workspace.Gate.Release();
            throw;
        }
    }

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

            await sandbox.StartDevServerAsync(cancellationToken);

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
    /// <c>npm install</c> runs only when the lockfile differs from the image's, which is what makes a cold
    /// workspace fast: the sandbox image already has the starter template's dependencies installed, so the
    /// common case is a tree copy and nothing else. A site whose agent added a package pays the install once.
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

        if (!install.Succeeded)
        {
            logger.LogInformation("Installing dependencies for {Site}.", site.Nanoid);

            var result = await sandbox.RunAsync(
                new SandboxCommand("npm", ["install", "--no-audit", "--no-fund"], TimeSpan.FromMinutes(5)),
                cancellationToken: cancellationToken);

            if (!result.Succeeded)
                throw new SandboxException(
                    "The site's dependencies could not be installed.", result.Output);
        }
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
