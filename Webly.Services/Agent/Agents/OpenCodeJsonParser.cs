using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Agent.Agents;

/// <summary>
/// Turns <c>opencode run --format json</c> into Webly's own events.
///
/// <b>Reconciled against the real CLI</b> (1.18.31, <c>openai/gpt-5.5</c>), from a recorded turn —
/// <c>OpenCodeJsonParserTests</c> drives this class with it. One JSON object per line, each with a <c>type</c>, a
/// <c>sessionID</c> and a <c>part</c>:
///
/// <list type="bullet">
/// <item><c>step_start</c> / <c>step_finish</c> bracket one model call. <c>step_finish</c> carries what that call
/// cost — <c>part.tokens</c> (<c>input</c>, <c>output</c>, <c>reasoning</c>, <c>cache.read</c>,
/// <c>cache.write</c>; input excludes the cache, as both providers count it) and <c>part.cost</c> in dollars —
/// and <c>part.reason</c>, which is <c>tool-calls</c> until the last one.</item>
/// <item><c>text</c> is a finished piece of prose with its <c>part.messageID</c>. Every step but the last is
/// narration ("I'm running the check now"); the last step's text is the answer. So the reply is the last step's
/// text — the same choice <see cref="ClaudeStreamJsonParser"/> makes by taking the <c>result</c> — and the
/// narration still reaches the chat live, as it is written.</item>
/// <item><c>tool_use</c> is a finished tool call: <c>part.tool</c> and <c>part.state.input</c>. Paths are
/// absolute in the workspace, and the sandbox agent rewrites the workspace's own prefix away on the way out.</item>
/// </list>
///
/// Text mode, which this replaced, printed the narration and the answer as one stream and the cost nowhere; it
/// also had no session id to resume, so a warm workspace continued "the last session in this directory".
/// </summary>
internal sealed class OpenCodeJsonParser(string model, Func<CodingAgentEvent, Task> onEvent, ILogger logger)
{
    private const string WorkspacePrefix = "/workspace/";

    /// <summary>The tools whose use means a file changed, with the input key that names it.</summary>
    private static readonly Dictionary<string, string> WriteTools = new(StringComparer.Ordinal)
    {
        ["edit"] = "filePath",
        ["write"] = "filePath",
        ["multiedit"] = "filePath"
    };

    private readonly StringBuilder _buffer = new();

    /// <summary>Each message's prose, in order, by message id.</summary>
    private readonly List<(string MessageId, string Text)> _texts = [];

    private string? _lastStepMessageId;

    public string? SessionId { get; private set; }

    /// <summary>The CLI's own account of a failure, when it gave one.</summary>
    public string? Error { get; private set; }

    /// <summary>The last step's prose, or everything said when the stream ended before a step finished.</summary>
    public string Reply
    {
        get
        {
            var final = _texts.Where(x => x.MessageId == _lastStepMessageId).Select(x => x.Text).ToList();

            return string.Join("\n\n", final.Count > 0 ? final : _texts.Select(x => x.Text)).Trim();
        }
    }

    public async Task HandleAsync(SandboxOutput output)
    {
        if (output.IsError)
        {
            logger.LogDebug("opencode stderr: {Text}", output.Text);
            return;
        }

        _buffer.Append(output.Text);

        while (true)
        {
            var text = _buffer.ToString();
            var newline = text.IndexOf('\n');

            if (newline < 0) break;

            var line = text[..newline].Trim();
            _buffer.Remove(0, newline + 1);

            if (line.Length == 0) continue;

            try
            {
                await HandleLineAsync(line);
            }
            catch (InvalidOperationException exception)
            {
                // A field of an unexpected JSON type. Skipped for the reason an unknown event is: this is another
                // product's interface, and one surprising field must not lose a turn's work.
                logger.LogDebug(exception, "Skipped an opencode json line that did not have the expected shape.");
            }
        }
    }

    private async Task HandleLineAsync(string line)
    {
        JsonNode? node;

        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return;
        }

        SessionId ??= node?["sessionID"]?.GetValue<string>();

        // The CLI's own account of a failure arrives without a part, so it is read before anything needs one.
        if (node?["type"]?.GetValue<string>() == "error")
        {
            var error = node["error"];

            Error = error?["data"]?["message"]?.GetValue<string>() ?? error?["message"]?.GetValue<string>() ?? error?.ToJsonString();
            return;
        }

        var part = node?["part"];

        if (part is null) return;

        switch (node?["type"]?.GetValue<string>())
        {
            case "text":
                if (part["text"]?.GetValue<string>() is not { Length: > 0 } value) break;

                var messageId = part["messageID"]?.GetValue<string>() ?? string.Empty;
                _texts.Add((messageId, value.Trim()));

                // A line between pieces, because each is a finished paragraph rather than a fragment of one.
                await onEvent(new CodingAgentEvent.Text($"{value.Trim()}\n\n"));
                break;

            case "tool_use":
                await ReportToolAsync(part);
                break;

            case "step_finish":
                _lastStepMessageId = part["messageID"]?.GetValue<string>() ?? _lastStepMessageId;

                var tokens = part["tokens"];

                await onEvent(new CodingAgentEvent.Usage(new AgentUsage(
                    model,
                    Count(tokens?["input"]),
                    Count(tokens?["output"]) + Count(tokens?["reasoning"]),
                    Count(tokens?["cache"]?["read"]),
                    Count(tokens?["cache"]?["write"]),
                    Number(part["cost"]))));

                break;
        }
    }

    private async Task ReportToolAsync(JsonNode part)
    {
        var tool = part["tool"]?.GetValue<string>() ?? "tool";
        var input = part["state"]?["input"];

        var paths = tool switch
        {
            _ when WriteTools.TryGetValue(tool, out var key) && input?[key]?.GetValue<string>() is { Length: > 0 } path
                => [path],
            // A patch names its files inside its text, one "*** Update File: path" line each.
            "apply_patch" or "patch" => PatchedFiles(input?["patchText"]?.GetValue<string>() ?? string.Empty),
            _ => []
        };

        foreach (var path in paths) await onEvent(new CodingAgentEvent.FileChanged(Relative(path)));

        if (tool != "todowrite")
            await onEvent(new CodingAgentEvent.Activity(Phrase(tool), paths.Count == 1 ? Relative(paths[0]) : null));
    }

    private static List<string> PatchedFiles(string patch) =>
    [
        .. patch.Split('\n')
            .Select(line => line.Trim())
            .Select(line => new[] { "*** Update File: ", "*** Add File: ", "*** Delete File: " }
                .Where(line.StartsWith)
                .Select(marker => line[marker.Length..].Trim())
                .FirstOrDefault())
            .OfType<string>()
            .Where(path => path.Length > 0)
            .Distinct()
    ];

    private static string Relative(string path)
    {
        var index = path.IndexOf(WorkspacePrefix, StringComparison.Ordinal);

        return index >= 0 ? path[(index + WorkspacePrefix.Length)..] : path.TrimStart('/');
    }

    /// <summary>The same words <see cref="ClaudeStreamJsonParser"/> uses, so the chat reads alike either way.</summary>
    private static string Phrase(string tool) => tool switch
    {
        "read" => "Reading your site",
        "write" => "Writing a file",
        "edit" or "multiedit" or "apply_patch" or "patch" => "Editing a file",
        "glob" or "grep" or "list" => "Looking through your site",
        "bash" => "Running a command",
        "webfetch" or "websearch" => "Looking something up",
        _ when tool.StartsWith(BrowserTools.OpenCodeToolPrefix, StringComparison.Ordinal) => BrowserTools.Activity,
        _ => "Working"
    };

    private static long Count(JsonNode? node) => (long)Number(node);

    private static decimal Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : 0m;
}
