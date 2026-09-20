using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Webly.Services.Services.Sandboxes;

/// <summary>
/// The sandbox agent as a plain child process on this machine, with a temporary directory for a workspace.
///
/// This is the provider a fresh clone uses, and the reason it exists is a plain one: the other two both need
/// something you might not have. E2B needs an account, and <see cref="DockerSandboxProvider"/> needs a Docker
/// daemon — which a lot of development machines, CI runners and containers do not have. With this one, editing
/// a site needs <b>node and git and nothing else</b>, which is what makes "clone it and try it" true.
///
/// <b>It is not isolation, and it must never be the production provider.</b> The workspace is a directory on
/// this host, the agent CLI runs as this user, and a command it runs can reach anything this process can —
/// including the database and the configuration. Every security property in <see cref="ISandbox"/>'s comment
/// is absent here. That is an acceptable trade on a machine whose owner is the person typing into the chat,
/// and only there, which is why <c>AddWeblySites</c> refuses to select it outside Development.
///
/// What it does keep is the contract. The agent it spawns is the same <c>tools/sandbox-agent</c> the image
/// runs, reached over HTTP with a per-sandbox bearer token, so files, exec, the dev server and the preview all
/// behave exactly as they do in production — which is the whole reason the provider boundary is where it is.
/// <c>tools/e2e/sandbox.mjs</c> starts it the same way from node, and <c>tools/e2e/run.mjs</c> drives a full
/// turn through it.
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

        // The random suffix is not decoration: a timestamp to the second is not unique, and two sandboxes that
        // shared a workspace directory would edit each other's files while each believed it was alone.
        var workspace = Path.Combine(
            Path.GetFullPath(_local.WorkspaceRoot),
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

        foreach (var (key, value) in spec.Environment) startInfo.Environment[key] = value;

        var process = Process.Start(startInfo)
            ?? throw new SandboxException("The local sandbox agent could not be started.");

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

    /// <summary>Under <c>.run/</c> by default, which is gitignored — a workspace is scratch space.</summary>
    public string WorkspaceRoot { get; set; } = ".run/workspaces";

    /// <summary>
    /// Off for a developer who wants to look at what the agent did to the files afterwards. On by default,
    /// because a workspace holds a full <c>node_modules</c> and leaving one per turn fills a disk.
    /// </summary>
    public bool DeleteWorkspaceOnStop { get; set; } = true;
}
