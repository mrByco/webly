using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Webly.Services.Services.Sandboxes;

/// <summary>
/// The sandbox agent as a plain child process on this machine, with a temporary directory for a workspace —
/// outside this checkout, for the reason <see cref="LocalSandboxOptions.ResolveWorkspaceRoot"/> gives.
///
/// This is the provider a fresh clone uses, and the reason it exists is a plain one: the other two both need
/// something you might not have. E2B needs an account, and <see cref="DockerSandboxProvider"/> needs a Docker
/// daemon — which a lot of development machines, CI runners and containers do not have. With this one, editing
/// a site needs <b>node and git and nothing else</b>, which is what makes "clone it and try it" true.
///
/// <b>It must never be the production provider.</b> The workspace is a directory on this host and the agent
/// CLI runs as this user. What keeps the agent inside its workspace is <b>bubblewrap</b>
/// (<see cref="LocalSandboxOptions.Confinement"/>): every command and the dev server see the workspace, a
/// private home, a read-only toolchain and nothing else of the machine — no checkout, no other site, no
/// repositories, no inherited secrets. It was a plain child process until the first real agent through it
/// committed to a site's bare repository on its own. What it does not do is filter the network, and a kernel
/// shared with the host is not a microVM, which is why <c>AddWeblySites</c> still refuses to select it
/// outside Development.
///
/// What it does keep is the contract. The agent it spawns is the same <c>tools/sandbox-agent</c> the image
/// runs, reached over HTTP with a per-sandbox bearer token, so files, exec, the dev server and the preview all
/// behave exactly as they do in production — which is the whole reason the provider boundary is where it is.
/// <c>tools/e2e/sandbox.mjs</c> starts it the same way from node, and <c>tools/e2e/run.mjs</c> drives a full
/// turn through it.
///
/// One cost to know about: a cold workspace here installs the site's dependencies, which is minutes and half a
/// gigabyte, because there is no image to have prebaked them. The workspace is then warm until it is reaped,
/// so it is the first turn after a restart that waits. <see cref="DockerSandboxProvider"/> is the fast path and
/// it is one configuration key away.
/// </summary>
public class LocalSandboxProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SandboxOptions> options,
    IOptions<LocalSandboxOptions> localOptions,
    ILogger<LocalSandboxProvider> logger) : ISandboxProvider
{
    public const string ProviderName = "local";

    public string Name => ProviderName;

    private readonly SandboxOptions _options = options.Value;
    private readonly LocalSandboxOptions _local = localOptions.Value;

    private int _sweptWorkspaceRoot;

    /// <summary>
    /// Removes every workspace left behind by a previous process, once, before the first sandbox starts.
    ///
    /// A workspace is deleted when its sandbox stops, which covers the ordinary path and nothing else: a
    /// crash, a stopped debugger or a <c>kill -9</c> leaves a directory holding a full <c>node_modules</c> —
    /// half a gigabyte each, one per site per run. Two of them had accumulated by the second day of anybody
    /// using this, which is the whole argument for the sweep.
    ///
    /// It is safe because this provider is per process in the same way <c>RunRegistry</c> is: every workspace
    /// under this root belongs to a sandbox this process started, so at the moment this process starts, none
    /// of them is owned by anybody. The exception is two instances sharing one root, which is not a thing
    /// development does and is another reason this provider refuses to run in production.
    /// </summary>
    private void SweepWorkspaceRootOnce(string root)
    {
        if (Interlocked.Exchange(ref _sweptWorkspaceRoot, 1) == 1) return;

        if (!Directory.Exists(root)) return;

        // The processes first, then their workspaces. The other order is the one that wedges a machine: a
        // `next dev` whose workspace has just been deleted does not exit, it spins at 100% of a core retrying
        // files that are not there any more.
        //
        // Both kinds, and the agent is the one that was missing. A dev server has been recorded and swept since
        // sixteen of them wedged a machine; the agent that spawned it was recorded nowhere, so a backend that was
        // killed rather than stopped left a node process per open site listening on a loopback port for ever —
        // fourteen of them, once, with their workspaces already deleted from under them by this very loop.
        foreach (var pidFile in Directory.EnumerateFiles(root, "*.devpid"))
        {
            KillRecorded(pidFile, "dev server");
        }

        foreach (var pidFile in Directory.EnumerateFiles(root, "*.agentpid"))
        {
            KillRecorded(pidFile, "sandbox agent");
        }

        foreach (var stale in Directory.EnumerateDirectories(root))
        {
            try
            {
                Directory.Delete(stale, recursive: true);
                logger.LogInformation("Removed the workspace {Workspace}, left by an earlier run.", stale);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Worth a line and nothing more: a directory that will not delete is disk, not correctness,
                // and refusing to start a turn over it would be the worse failure.
                logger.LogWarning(exception, "Could not remove the stale workspace {Workspace}.", stale);
            }
        }
    }

    /// <summary>
    /// Kills a process an earlier run recorded, and forgets the record.
    ///
    /// Development only, like everything in this class, and defensive by construction: a pid file from a
    /// previous boot can name a process that has exited, or — after enough churn — one that belongs to
    /// something else entirely. That is why the whole tree goes rather than a process group: a stale pid names
    /// something with no children of ours, and the kill is then one signal to one process that is not there.
    /// </summary>
    private void KillRecorded(string pidFile, string what)
    {
        try
        {
            if (int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid) && pid > 1)
            {
                using var process = Process.GetProcessById(pid);

                process.Kill(entireProcessTree: true);
                logger.LogInformation("Killed the {What} {Pid}, left by an earlier run.", what, pid);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or IOException or UnauthorizedAccessException or FormatException)
        {
            // The process is gone, or the file is unreadable. Either way there is nothing to kill.
        }
        finally
        {
            try
            {
                File.Delete(pidFile);
            }
            catch (IOException)
            {
                // Disk, not correctness.
            }
        }
    }

    public async Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken = default)
    {
        var agentScript = Path.GetFullPath(_local.AgentPath);

        if (!File.Exists(agentScript))
            throw new SandboxException(
                "The local sandbox agent could not be found.",
                $"Expected {agentScript}. Set Sandbox:Local:AgentPath, or run from the repository root.");

        // Checked, not trusted, for the same reason GitSiteRepositoryStore.PathFor checks it: this is a path
        // built from a value that arrived over HTTP, and one traversal here would put a workspace anywhere.
        if (!spec.SiteNanoid.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            throw new SandboxException("That site's id is not one this provider will build a path from.");

        var confined = await EnsureConfinementAsync();

        // The random suffix is not decoration: a timestamp to the second is not unique, and two sandboxes that
        // shared a workspace directory would edit each other's files while each believed it was alone.
        var workspaceRoot = _local.ResolveWorkspaceRoot(agentScript);

        SweepWorkspaceRootOnce(workspaceRoot);

        var workspace = Path.Combine(
            workspaceRoot,
            $"{spec.SiteNanoid}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(workspace);

        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

        // Ports are picked here rather than left to the agent, because the caller has to know the agent's
        // port to reach it and there is no container boundary to fix them behind.
        var agentPort = FreePort();
        var devPort = FreePort();

        var startInfo = new ProcessStartInfo(_local.NodePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workspace
        };

        startInfo.ArgumentList.Add(agentScript);

        startInfo.Environment["WEBLY_WORKSPACE"] = workspace;
        startInfo.Environment["WEBLY_AGENT_PORT"] = agentPort.ToString();
        startInfo.Environment["WEBLY_AGENT_TOKEN"] = token;
        startInfo.Environment["WEBLY_DEV_PORT"] = devPort.ToString();

        // Where the agent records its dev server's process id, beside the workspace rather than inside it — a
        // file in the workspace would travel into the site's next commit. It is the sweep's only way to find a
        // dev server whose agent was killed outright: `next dev` is spawned into its own process group, so a
        // backend that was stopped rather than asked leaves one behind, holding a port and a few hundred
        // megabytes, once per open site.
        startInfo.Environment["WEBLY_DEV_PIDFILE"] = $"{workspace}.devpid";

        foreach (var (key, value) in spec.Environment) startInfo.Environment[key] = value;

        if (confined)
        {
            startInfo.Environment["WEBLY_CONFINE"] = LocalSandboxOptions.BubblewrapConfinement;

            // A confined command's environment is an allow-list, so the workload's own variables are named.
            startInfo.Environment["WEBLY_WORKLOAD_ENV"] = string.Join(',', spec.Environment.Keys);
        }

        var process = Process.Start(startInfo)
            ?? throw new SandboxException("The local sandbox agent could not be started.");

        // The agent's own pid, written by this side rather than by the agent, because this side is what knows
        // it — and beside the workspace rather than inside it, for the same reason the dev server's is: a file
        // in the workspace travels into the site's next commit. Best-effort: a pid file that cannot be written
        // is a sandbox that has to be cleaned up by hand later, which is not a reason to fail the turn now.
        var agentPidFile = $"{workspace}.agentpid";

        try
        {
            File.WriteAllText(agentPidFile, process.Id.ToString());
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not record the sandbox agent's process id at {Path}.", agentPidFile);
        }

        // Read both streams, or a chatty agent fills its pipe buffer and blocks. Logged at debug: this is the
        // agent's own output, not the site's, and the dev server's log is fetched over the contract instead.
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogDebug("sandbox-agent: {Line}", e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logger.LogDebug("sandbox-agent: {Line}", e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        logger.LogInformation(
            "Started a local sandbox for site {Site} on port {Port}, workspace {Workspace}.",
            spec.SiteNanoid, agentPort, workspace);

        var sandbox = new SandboxAgentClient(
            $"local-{agentPort}",
            new Uri($"http://127.0.0.1:{agentPort}/"),
            token,
            httpClientFactory.CreateClient(SandboxAgentClient.HttpClientName),
            () =>
            {
                try
                {
                    // The whole tree, because the dev server is a child of the agent: killing only the agent
                    // leaves `next dev` holding its port, and the next sandbox on that port would answer with
                    // somebody else's site.
                    if (!process.HasExited) process.Kill(entireProcessTree: true);

                    // Waited for, before the directory goes. Kill is asynchronous, and a dev server that is
                    // still alive when its workspace disappears does not exit — it spins at 100% of a core
                    // retrying files that are no longer there, for ever. Several of those at once is what a
                    // wedged machine looks like, and nothing in the logs says why.
                    process.WaitForExit(milliseconds: 5_000);
                }
                catch (InvalidOperationException)
                {
                    // Already gone. Nothing to do, and nothing worth logging.
                }

                foreach (var record in new[] { $"{workspace}.devpid", agentPidFile })
                {
                    try
                    {
                        File.Delete(record);
                    }
                    catch (IOException)
                    {
                        // Disk, not correctness. The sweep at the next start will find it.
                    }
                }

                if (!_local.DeleteWorkspaceOnStop)
                {
                    // Nothing to do; the developer asked to keep it.
                }
                else if (!process.HasExited)
                {
                    // Deliberately left on disk. See the comment above: deleting it under a process that is
                    // still running is worse than a stale directory under .run/, and this says so out loud
                    // rather than silently skipping.
                    logger.LogWarning(
                        "The sandbox agent for {Workspace} did not exit, so its workspace was left in place.",
                        workspace);
                }
                else
                {
                    try
                    {
                        Directory.Delete(workspace, recursive: true);

                        // The agent CLI's home, beside the workspace: its sessions, which die with it.
                        if (Directory.Exists($"{workspace}.home")) Directory.Delete($"{workspace}.home", recursive: true);
                    }
                    catch (IOException exception)
                    {
                        // Something else holding a file open. Untidy rather than harmful: it is under .run/.
                        logger.LogDebug(exception, "Could not delete the workspace at {Workspace}.", workspace);
                    }
                }

                return Task.CompletedTask;
            },
            logger);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.StartupTimeout);

        while (!await sandbox.IsHealthyAsync(deadline.Token))
        {
            if (process.HasExited)
            {
                await sandbox.DisposeAsync();

                throw new SandboxException(
                    "The local sandbox agent stopped immediately.",
                    $"node exited {process.ExitCode}. Is {_local.NodePath} on PATH?");
            }

            if (deadline.IsCancellationRequested)
            {
                await sandbox.DisposeAsync();

                throw new SandboxException("The local sandbox agent never became reachable.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), deadline.Token);
        }

        return sandbox;
    }

    private Task<bool>? _confinement;

    /// <summary>
    /// Whether this provider confines its sandboxes, decided once: false only when configuration says
    /// <c>none</c>, and a refusal when it asks for bubblewrap and bubblewrap cannot make a sandbox here.
    ///
    /// Proven by running one, not by finding the binary: bubblewrap is installed on plenty of machines that
    /// forbid the user namespaces it needs, and there every command of every turn would fail with bwrap's own
    /// sentence about namespaces — in the chat, as the reason somebody's website did not change.
    /// </summary>
    private async Task<bool> EnsureConfinementAsync()
    {
        if (string.Equals(_local.Confinement, LocalSandboxOptions.NoConfinement, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(_local.Confinement, LocalSandboxOptions.BubblewrapConfinement, StringComparison.OrdinalIgnoreCase))
            throw new SandboxException(
                "The local sandbox is misconfigured.",
                $"Sandbox:Local:Confinement is '{_local.Confinement}'; it must be 'bubblewrap' or 'none'.");

        var works = await (_confinement ??= ProbeBubblewrapAsync());

        if (!works)
            throw new SandboxException(
                "Editing a site needs a sandbox this machine cannot make.",
                "The local provider confines the agent to its workspace with bubblewrap, and `bwrap` is missing or "
                + "cannot create a sandbox here. Install it (apt install bubblewrap / dnf install bubblewrap), or set "
                + "Sandbox:Provider to 'docker'. Sandbox:Local:Confinement = 'none' runs the agent unconfined, with "
                + "access to everything this user can read.");

        return true;
    }

    private async Task<bool> ProbeBubblewrapAsync()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("bwrap")
            {
                ArgumentList = { "--ro-bind", "/", "/", "--unshare-pid", "--die-with-parent", "true" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (probe is null) return false;

            var error = probe.StandardError.ReadToEndAsync();
            await probe.WaitForExitAsync();

            if (probe.ExitCode != 0)
                logger.LogWarning("bubblewrap is installed but cannot make a sandbox: {Error}", (await error).Trim());

            return probe.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Not installed: Process.Start cannot find the file.
            return false;
        }
    }

    /// <summary>
    /// A free port, by binding one and letting go. Racy in principle — something else can take it in the
    /// gap — and the right amount of engineering for a development-only provider, where the alternative is a
    /// registry of ports that has to survive a restart.
    /// </summary>
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>Where the local provider finds node, the agent, and somewhere to put workspaces.</summary>
public class LocalSandboxOptions
{
    public const string SectionName = "Sandbox:Local";

    /// <summary>
    /// The sandbox agent's entry point, relative to the working directory. The real one, not a copy: the
    /// point of this provider is that the contract under test is the contract that ships.
    /// </summary>
    public string AgentPath { get; set; } = "tools/sandbox-agent/index.js";

    public string NodePath { get; set; } = "node";

    /// <summary>
    /// Empty by default, which means a directory of this checkout's own under the system's temporary directory —
    /// see <see cref="ResolveWorkspaceRoot"/>.
    /// </summary>
    public string WorkspaceRoot { get; set; } = string.Empty;

    /// <summary>
    /// Where workspaces go: the configured root, or <c>{temp}/webly-workspaces-{checkout}</c>.
    ///
    /// <b>Never inside a git repository</b>, and it used to be: the default was <c>.run/workspaces</c>, which is
    /// inside this checkout. A workspace deliberately has no <c>.git</c> of its own — the agent never sees git —
    /// so a coding agent that looks for its project by walking up for one finds <i>Webly's</i>: asked from there,
    /// OpenCode reports "Workspace root folder: /home/…/webly — is a git repo: yes". Claude Code reads every
    /// <c>CLAUDE.md</c> on the same walk, which would hand it this repository's own instructions. Together with
    /// an inherited <c>PWD</c> (the sandbox agent's <c>environmentFor</c>), it is why the first turn OpenCode ever
    /// ran through the app began "This is the main Webly repository, not an obvious checked-out site" and went
    /// looking through the other sites' bare repositories. In the image the workspace is <c>/workspace</c> and
    /// there is nothing above it, so this was a difference between development and production in exactly the
    /// place the local provider exists to have none.
    ///
    /// Per checkout rather than one shared directory, because the first-use sweep deletes everything under the
    /// root and kills what it recorded: two checkouts sharing one would each take the other's sandboxes down on
    /// start, which <c>.run/workspaces</c> never could. The agent script's own path names the checkout.
    /// </summary>
    public string ResolveWorkspaceRoot(string agentScript)
    {
        if (!string.IsNullOrWhiteSpace(WorkspaceRoot)) return Path.GetFullPath(WorkspaceRoot);

        var checkout = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(agentScript)))[..8].ToLowerInvariant();

        return Path.Combine(Path.GetTempPath(), $"webly-workspaces-{checkout}");
    }

    /// <summary>
    /// <c>bubblewrap</c> (the default) or <c>none</c>: whether what the agent runs can reach anything of this
    /// machine but its workspace. See the sandbox agent's <c>CONFINE</c> for what it can and cannot see.
    ///
    /// <b>Required by default, and refused rather than skipped when it is missing</b>, because the agent must not
    /// reach anything outside its working directory and the first real one through this provider did — it found
    /// a site's bare repository and committed to it. So a machine without bubblewrap (macOS, Windows, a Linux
    /// that forbids user namespaces) gets a sentence naming the remedies instead of an unconfined agent: install
    /// it, or use the <c>docker</c> provider, whose container is the same boundary. <c>none</c> is the explicit
    /// way back to an unconfined process, for somebody who has decided that on their own machine.
    /// </summary>
    public string Confinement { get; set; } = BubblewrapConfinement;

    public const string BubblewrapConfinement = "bubblewrap";

    public const string NoConfinement = "none";

    /// <summary>
    /// Off for a developer who wants to look at what the agent did to the files afterwards. On by default,
    /// because a workspace holds a full <c>node_modules</c> and leaving one per turn fills a disk.
    /// </summary>
    public bool DeleteWorkspaceOnStop { get; set; } = true;
}
