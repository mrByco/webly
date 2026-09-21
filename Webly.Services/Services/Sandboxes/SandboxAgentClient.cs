using Microsoft.Extensions.Logging;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
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
    /// <b>Retried until the dev server is listening</b>, and that is the whole point of the method. On a cold
    /// workspace this runs seconds after <c>next dev</c> was spawned, and the agent answers 502 the moment its
    /// proxy cannot connect — so the one request arrived before anything was listening, nothing ever compiled, the
    /// log stayed at the startup banner, and the build check read that as "the site is fine". A site with a syntax
    /// error in its home page reported a clean turn and served a 500 in the preview pane.
    ///
    /// Only that one answer is retried. A 500 from the dev server is a compiled page that threw, which is
    /// precisely the thing being looked for, and anything else the log describes better than a status code.
    ///
    /// Other failures are swallowed on purpose: this is not a health check and its answer is not used, and a
    /// sandbox that has gone away is not a reason to fail a turn that has already done its work.
    /// </summary>
    public async Task TouchPreviewAsync(CancellationToken cancellationToken = default)
    {
        // Fifteen seconds, which is generous against a `next dev` that reports ready in about one and mean
        // against nothing: the alternative to waiting is reporting on a log that has not been written yet.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (true)
        {
            try
            {
                using var response = await SendAsync(HttpMethod.Get, "preview/", null, cancellationToken);

                // Read the body, so that compilation finishes rather than being abandoned mid-response.
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                var notUpYet = response.StatusCode == HttpStatusCode.BadGateway
                    && body.Contains("not answering yet", StringComparison.OrdinalIgnoreCase);

                if (!notUpYet || DateTime.UtcNow >= deadline) return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogDebug(exception, "Touching the preview of sandbox {Sandbox} failed.", id);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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
            body?["offset"]?.GetValue<long>() ?? 0,
            // Absent means an older sandbox agent, which only ever answered while its dev server was alive.
            body?["running"]?.GetValue<bool>() ?? true);
    }

    public async Task<SandboxHealth> ReadHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "health", null, cancellationToken);

            if (!response.IsSuccessStatusCode) return SandboxHealth.Unreachable;

            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            // "stopped" is the agent's word for no dev server; "ready" and "starting" both mean there is one.
            var devServer = body?["devServer"]?.GetValue<string>();

            return new SandboxHealth(true, devServer is not null and not "stopped");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return SandboxHealth.Unreachable;
        }
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
