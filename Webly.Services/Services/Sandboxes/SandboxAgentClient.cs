using Microsoft.Extensions.Logging;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webly.Services.Services.Repositories;

namespace Webly.Services.Services.Sandboxes;

/// <summary>
/// <see cref="ISandbox"/> over the sandbox agent's HTTP contract (<c>tools/sandbox-agent</c>).
///
/// The whole of the file, process and preview mechanics live here, once, for every provider — see
/// <see cref="ISandboxProvider"/> for why. A provider hands this class a URL and a token and is done.
///
/// Tar over the wire, with <c>System.Formats.Tar</c> on this side and the <c>tar</c> binary on the other:
/// one request carries a whole tree, the format is the one thing every image already has, and neither side
/// needs a dependency for it.
/// </summary>
public sealed class SandboxAgentClient(
    string id,
    Uri agentUrl,
    string agentToken,
    HttpClient httpClient,
    Func<Task> stopAsync,
    ILogger logger) : ISandbox
{
    /// <summary>
    /// The named <c>HttpClient</c> every call into a sandbox uses, registered in <c>AddWeblySites</c>.
    ///
    /// Named rather than typed, and asked for per sandbox, because the providers are singletons: a typed client
    /// injected into a singleton lives for the life of the process, which is precisely what
    /// <c>IHttpClientFactory</c> exists to prevent — its handler never rotates and a DNS change is never picked
    /// up. Holding one per sandbox is the right span, since a sandbox's address is fixed for its lifetime.
    /// </summary>
    public const string HttpClientName = "sandbox";

    public string Id => id;
    public Uri AgentUrl => agentUrl;
    public string AgentToken => agentToken;

    public async Task WriteTreeAsync(WorkspaceTree tree, CancellationToken cancellationToken = default)
    {
        using var archive = new MemoryStream();

        await using (var gzip = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
        await using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var file in tree.Files)
            {
                // 0644 and no owner: the tree came from a repository, and a mode or a uid carried from
                // somewhere else is a way to hand the sandbox something executable.
                var entry = new PaxTarEntry(TarEntryType.RegularFile, $"./{file.Path}")
                {
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                    DataStream = new MemoryStream(file.Content)
                };

                await writer.WriteEntryAsync(entry, cancellationToken);
            }
        }

        archive.Position = 0;

        using var content = new StreamContent(archive);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");

        using var response = await SendAsync(HttpMethod.Post, "files", content, cancellationToken);
        await EnsureOkAsync(response, "writing the workspace", cancellationToken);
    }

    public async Task<WorkspaceTree> ReadTreeAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "files", null, cancellationToken);
        await EnsureOkAsync(response, "reading the workspace", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);

        var files = new List<WorkspaceFile>();

        while (await reader.GetNextEntryAsync(copyData: true, cancellationToken) is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
            if (entry.DataStream is null) continue;

            // "./src/app/page.tsx" → "src/app/page.tsx". One spelling of a path reaches git, because the
            // repository is what these get compared against on the next commit.
            var path = entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;

            if (path.Length == 0) continue;

            using var buffer = new MemoryStream();
            await entry.DataStream.CopyToAsync(buffer, cancellationToken);

            files.Add(new WorkspaceFile(path, buffer.ToArray()));
        }

        return new WorkspaceTree(files);
    }

    /// <summary>
    /// Runs a command and reads the agent's NDJSON stream as it arrives, so a caller can forward each line
    /// to whoever is watching. Reading the whole response first would turn a two-minute agent turn into two
    /// minutes of nothing followed by everything.
    /// </summary>
    public async Task<SandboxCommandResult> RunAsync(
        SandboxCommand command,
        Func<SandboxOutput, Task>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["command"] = command.Command,
            ["args"] = new JsonArray([.. command.Arguments.Select(x => (JsonNode)JsonValue.Create(x)!)]),
            ["timeoutMs"] = (long)(command.Timeout ?? TimeSpan.FromMinutes(10)).TotalMilliseconds
        };

        if (command.Environment is { Count: > 0 })
        {
            var environment = new JsonObject();

            foreach (var (key, value) in command.Environment) environment[key] = value;

            payload["env"] = environment;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Url("exec"))
        {
            Content = JsonContent.Create(payload)
        };

        Authorize(request);

        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        await EnsureOkAsync(response, $"running {command.Command}", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var output = new System.Text.StringBuilder();
        var exitCode = -1;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0) continue;

            JsonNode? node;

            try
            {
                node = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                // A line the agent did not write — a proxy's keep-alive, a truncated chunk. Skipping it is
                // right: the exit event is what decides the outcome, not the completeness of the log.
                logger.LogDebug("Unparseable line from sandbox {Sandbox}: {Line}", id, line);
                continue;
            }

            var type = node?["type"]?.GetValue<string>();

            if (type == "exit")
            {
                exitCode = node?["code"]?.GetValue<int>() ?? -1;
                continue;
            }

            var text = node?["text"]?.GetValue<string>() ?? string.Empty;
            output.Append(text);

            if (onOutput is not null) await onOutput(new SandboxOutput(type == "stderr", text));
        }

        return new SandboxCommandResult(exitCode, output.ToString());
    }

    public async Task StartDevServerAsync(string basePath, CancellationToken cancellationToken = default)
    {
        using var content = JsonContent.Create(new JsonObject
        {
            ["command"] = "npm",
            ["args"] = new JsonArray("run", "dev"),

            // The agent passes this to the dev server as WEBLY_PREVIEW_BASE and rewrites everything under
            // /preview onto it, so that the URLs in the served HTML and the URLs the browser asks Webly for are
            // the same path. See `templates/next-site/next.config.ts`.
            ["basePath"] = basePath
        });

        using var response = await SendAsync(HttpMethod.Post, "dev/start", content, cancellationToken);
        await EnsureOkAsync(response, "starting the dev server", cancellationToken);
    }

    /// <summary>
    /// One request through the agent's own preview proxy, which is what makes the dev server compile.
    ///
    /// Failures are swallowed on purpose. This is not a health check and its answer is not used: a dev server
    /// that is still starting, or a page that throws, are both things the log will describe better than a
    /// status code would, and neither is a reason to fail a turn that has already done its work.
    /// </summary>
    public async Task TouchPreviewAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "preview/", null, cancellationToken);

            // Read the body, so that compilation finishes rather than being abandoned mid-response.
            await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Touching the preview of sandbox {Sandbox} failed.", id);
        }
    }

    public async Task<DevServerLog> ReadDevServerLogAsync(
        long since = 0,
        CancellationToken cancellationToken = default)
    {
        var path = since > 0 ? $"dev/log?since={since}" : "dev/log";

        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);

        if (!response.IsSuccessStatusCode) return DevServerLog.Empty;

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return new DevServerLog(
            body?["log"]?.GetValue<string>() ?? string.Empty,
            body?["offset"]?.GetValue<long>() ?? 0);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "health", null, cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>Stops the machine. The provider supplied the how; this class only knows when.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await stopAsync();
        }
        catch (Exception exception)
        {
            // A sandbox that would not stop is money, not correctness: log it loudly so the provider's own
            // idle timeout is the backstop rather than the only line of defence.
            logger.LogError(exception, "Sandbox {Sandbox} could not be stopped.", id);
        }
    }

    private Uri Url(string path) => new(agentUrl, path);

    private void Authorize(HttpRequestMessage request) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, Url(path)) { Content = content };
        Authorize(request);

        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task EnsureOkAsync(
        HttpResponseMessage response,
        string what,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        throw new SandboxException($"The sandbox failed while {what}.", $"{(int)response.StatusCode}: {body}");
    }
}
