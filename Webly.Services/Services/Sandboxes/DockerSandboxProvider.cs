using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

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

    public async Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken = default)
    {
        var agentToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var name = $"webly-sandbox-{spec.SiteNanoid.ToLowerInvariant()}-{DateTime.UtcNow.Ticks % 100000}";

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

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new SandboxException($"docker {arguments[0]} failed.", error);

        return output;
    }
}
