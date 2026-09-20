using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Webly.Services.Services.Sandboxes;

/// <summary>
/// Sandboxes on E2B: start a machine from Webly's template, wait for its agent to answer, hand back a
/// <see cref="SandboxAgentClient"/>.
///
/// Its whole job is those three steps. Everything a workspace actually does — files, commands, the dev
/// server, the preview — goes over the agent contract, so this class does not touch E2B's filesystem or
/// process APIs at all. That is what makes a second provider small, and it is also what keeps the
/// unverified surface here down to two REST calls.
///
/// <b>Unverified against the live API.</b> The two endpoints and the port hostname pattern follow E2B's
/// documented REST shape, but nothing here has run against the service — there was no key in the
/// environment where it was written. See whats_next.md; the first task with a key is to create a sandbox,
/// hit <c>/health</c> through the exposed port, and reconcile.
/// </summary>
public class E2bSandboxProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SandboxOptions> options,
    ILogger<E2bSandboxProvider> logger) : ISandboxProvider
{
    public const string ProviderName = "e2b";

    public string Name => ProviderName;

    private readonly SandboxOptions _options = options.Value;

    public async Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken = default)
    {
        var e2b = _options.E2b;

        if (!e2b.IsConfigured)
            throw new SandboxException("Sandboxes are not configured in this environment.");

        // The token is minted here, per sandbox, and given to the machine as an environment variable. The
        // sandbox's URL is guessable and reachable from the internet; this is what makes it useless to
        // anybody who has not been told the token, including another customer's sandbox.
        var agentToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

        var environment = new JsonObject
        {
            ["WEBLY_AGENT_TOKEN"] = agentToken,
            ["WEBLY_WORKSPACE"] = "/workspace",
            ["WEBLY_AGENT_PORT"] = "8080",
            ["WEBLY_DEV_PORT"] = "3000"
        };

        foreach (var (key, value) in spec.Environment) environment[key] = value;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{e2b.BaseUrl.TrimEnd('/')}/sandboxes")
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["templateID"] = e2b.TemplateId,
                // The provider's own idle kill, in seconds: a belt to the reaper's braces, because a
                // process that crashes with a sandbox running must not leave it running for a month.
                ["timeout"] = (int)_options.MaxLifetime.TotalSeconds,
                ["metadata"] = new JsonObject { ["site"] = spec.SiteNanoid },
                ["envVars"] = environment
            })
        };

        request.Headers.Add("X-API-Key", e2b.ApiKey);

        var httpClient = httpClientFactory.CreateClient(SandboxAgentClient.HttpClientName);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new SandboxException("A sandbox could not be started.", $"{(int)response.StatusCode}: {body}");

        var sandboxId = JsonNode.Parse(body)?["sandboxID"]?.GetValue<string>()
            ?? throw new SandboxException("The sandbox provider did not return an id.", body);

        var agentUrl = new Uri(e2b.PortHostPattern
            .Replace("{port}", "8080")
            .Replace("{id}", sandboxId)
            .TrimEnd('/') + "/");

        logger.LogInformation("Started sandbox {Sandbox} for site {Site} at {Url}", sandboxId, spec.SiteNanoid, agentUrl);

        var sandbox = new SandboxAgentClient(
            sandboxId, agentUrl, agentToken, httpClientFactory.CreateClient(SandboxAgentClient.HttpClientName),
            () => StopAsync(sandboxId, cancellationToken: CancellationToken.None),
            logger);

        await WaitUntilHealthyAsync(sandbox, cancellationToken);

        return sandbox;
    }

    /// <summary>
    /// Polls the agent until it answers. A sandbox that is "created" is not yet a sandbox that can take a
    /// tree, and the difference is a few seconds of image start — without this wait the first write fails
    /// and the person sees an error for something that was merely not ready.
    /// </summary>
    private async Task WaitUntilHealthyAsync(ISandbox sandbox, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.StartupTimeout);

        while (!deadline.IsCancellationRequested)
        {
            if (await sandbox.IsHealthyAsync(deadline.Token)) return;

            await Task.Delay(TimeSpan.FromMilliseconds(500), deadline.Token);
        }

        await sandbox.DisposeAsync();

        throw new SandboxException("A sandbox started but never became reachable.");
    }

    private async Task StopAsync(string sandboxId, CancellationToken cancellationToken)
    {
        var e2b = _options.E2b;

        using var request = new HttpRequestMessage(
            HttpMethod.Delete, $"{e2b.BaseUrl.TrimEnd('/')}/sandboxes/{sandboxId}");

        request.Headers.Add("X-API-Key", e2b.ApiKey);

        var httpClient = httpClientFactory.CreateClient(SandboxAgentClient.HttpClientName);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            logger.LogWarning("Stopping sandbox {Sandbox} answered {Status}.", sandboxId, (int)response.StatusCode);
    }
}
