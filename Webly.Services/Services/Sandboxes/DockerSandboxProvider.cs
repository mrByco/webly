using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Webly.Services.Services.Sandboxes;

/// <summary>
/// Sandboxes as local Docker containers, for development.
///
/// The managed provider is what production uses; this exists so that a fresh clone can edit a site on a
/// laptop without a vendor account, which is the same rule the rest of the repository follows — an
/// unconfigured feature is absent, not broken. It is also the only provider that can be debugged by
/// attaching to the thing.
///
/// Not for production, and the reason is worth stating rather than implying: a container on the API's own
/// host shares that host's kernel and its network with the database. The isolation a coding agent needs is
/// the whole permission model (see <see cref="ISandbox"/>), so running it next to Postgres is exactly the
/// arrangement that makes the model untrue.
/// </summary>
public class DockerSandboxProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SandboxOptions> options,
    ILogger<DockerSandboxProvider> logger) : ISandboxProvider
{
    public const string ProviderName = "docker";

    public string Name => ProviderName;

    private readonly SandboxOptions _options = options.Value;

    private int _swept;

    /// <summary>The prefix every container this provider starts is named with, and what the sweep looks for.</summary>
    private const string NamePrefix = "webly-sandbox-";

    /// <summary>
    /// Removes every sandbox container left behind by a previous process, once, before the first one starts.
    ///
    /// A container is removed when its sandbox is disposed, which covers the ordinary path and nothing else.
    /// A crash, a stopped debugger, or simply restarting the API leaves one container per open site running —
    /// with a <c>next dev</c> inside it, holding memory and a core, for ever. <see cref="LocalSandboxProvider"/>
    /// has had this sweep since the day sixteen orphaned dev servers wedged a machine; this provider was
    /// written without it and nobody had noticed, because nobody had run it.
    ///
    /// Safe for the same reason the local one is: this provider is per process, so at the moment the process
    /// starts, no container carrying its name prefix belongs to anybody.
    ///
    /// Best-effort, deliberately. If docker is not there to answer, the next call will say so properly — a
    /// sweep that throws would turn "tidy up" into "cannot start a sandbox at all".
    /// </summary>
    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _swept, 1) == 1) return;

        try
        {
            var names = (await RunDockerAsync(
                    cancellationToken, "ps", "--all", "--quiet", "--filter", $"name={NamePrefix}"))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();

            if (names.Count == 0) return;

            logger.LogInformation("Removing {Count} sandbox container(s) left by a previous run.", names.Count);

            await RunDockerAsync(cancellationToken, ["rm", "--force", .. names]);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not sweep leftover sandbox containers.");
        }
    }

    public async Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken = default)
    {
        await SweepOnceAsync(cancellationToken);

        var agentToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        // The nanoid as it is: docker allows upper case in a container name, and lower-casing it means two
        // sites whose ids differ only in case share one name — which the teardown then removes by name.
        var name = $"{NamePrefix}{spec.SiteNanoid}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}";

        var arguments = new List<string>
        {
            "run", "--detach", "--rm", "--name", name,
            // Published on an ephemeral host port, bound to loopback: the container is reachable from this
            // process and from nothing else on the network.
            "--publish", "127.0.0.1::8080",
            "--env", $"WEBLY_AGENT_TOKEN={agentToken}",
            "--env", "WEBLY_WORKSPACE=/workspace",
            "--env", "WEBLY_AGENT_PORT=8080",
            "--env", "WEBLY_DEV_PORT=3000",
            // Modest caps, so a runaway build on a developer's machine is annoying rather than fatal.
            "--memory", "2g", "--cpus", "2"
        };

        foreach (var (key, value) in spec.Environment)
        {
            arguments.Add("--env");
            arguments.Add($"{key}={value}");
        }

        arguments.Add(_options.Image);

        await RunDockerAsync(cancellationToken, [.. arguments]);

        var port = (await RunDockerAsync(cancellationToken, "port", name, "8080/tcp"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Split(':').LastOrDefault()?.Trim()
            ?? throw new SandboxException("The sandbox container published no port.");

        var agentUrl = new Uri($"http://127.0.0.1:{port}/");

        logger.LogInformation("Started container {Name} for site {Site} at {Url}", name, spec.SiteNanoid, agentUrl);

        var sandbox = new SandboxAgentClient(
            name, agentUrl, agentToken, httpClientFactory.CreateClient(SandboxAgentClient.HttpClientName),
            async () => await RunDockerAsync(CancellationToken.None, "rm", "--force", name),
            logger);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.StartupTimeout);

        while (!await sandbox.IsHealthyAsync(deadline.Token))
        {
            if (deadline.IsCancellationRequested)
            {
                await sandbox.DisposeAsync();

                throw new SandboxException("The sandbox container never became reachable.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300), deadline.Token);
        }

        return sandbox;
    }

    private async Task<string> RunDockerAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new SandboxException("docker could not be started. Is Docker Desktop running?");

        // Both pipes drained at once, and that is not tidiness — reading one to the end while the other fills
        // is a deadlock, and `docker run` is the command that reaches it. The first run on any machine pulls
        // the image, and a pull writes its progress to **stderr**, megabytes of it: the pipe buffer is about
        // 64 KB, so the child blocks writing stderr while this blocks reading stdout, and neither moves again.
        // Demonstrated with a child that writes 4000 lines to stderr and one to stdout: sequential reads hang
        // for ever, concurrent reads finish.
        //
        // `GitSiteRepositoryStore` has always had this right, because it has always been run. This provider
        // has not.
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new SandboxException($"docker {arguments[0]} failed.", error);

        return output;
    }
}
