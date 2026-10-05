using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Agent.Agents;

/// <summary>
/// Turns <c>claude -p --output-format stream-json --verbose</c> into Webly's own events.
///
/// <b>Reconciled against the real CLI</b> (2.1.x), from a recorded turn on the starter template —
/// <c>tools/e2e/fixtures/claude-stream-json.ndjson</c> is that transcript, and
/// <c>ClaudeStreamJsonParserTests</c> drives this class with it. What the recording settled:
///
/// <list type="bullet">
/// <item><c>session_id</c> is on the <i>envelope</i>, not inside <c>message</c>. Every event carries it.</item>
/// <item>The stream is full of events this has no opinion about — <c>active_goal</c>,
/// <c>autocompact_state</c>, <c>rate_limit_event</c>, <c>system</c>, <c>user</c> (tool results). Skipping
/// what it does not recognise is therefore the normal case, not a safety net.</item>
/// <item>Assistant content blocks come in three kinds: <c>text</c>, <c>tool_use</c> and <c>thinking</c>.
/// Thinking is deliberately dropped: it is the model reasoning aloud, not an answer to show somebody.</item>
/// <item>The final <c>result</c> event carries the whole reply in <c>result</c>, and it is the authority —
/// the streamed text blocks include mid-turn narration ("now let me update…") that the final message does
/// not. So the reply is replaced by it rather than accumulated, unless <c>is_error</c> says it is a
/// failure message rather than a reply.</item>
/// <item><c>Edit</c> and <c>Write</c> name their target in <c>input.file_path</c>, absolute; in a sandbox
/// that is always under <c>/workspace</c>.</item>
/// </list>
/// </summary>
internal sealed class ClaudeStreamJsonParser(Func<CodingAgentEvent, Task> onEvent, ILogger logger)
{
    /// <summary>Where a sandbox's tree always lives, and therefore the prefix a reported path carries.</summary>
    private const string WorkspacePrefix = "/workspace/";

    /// <summary>
    /// The tools whose use means a file changed, with the key each one names it by. The editor lists these
    /// under the turn, so a tool that only reads must not appear here.
    /// </summary>
    private static readonly Dictionary<string, string> WriteTools = new(StringComparer.Ordinal)
    {
        ["Write"] = "file_path",
        ["Edit"] = "file_path",
        ["MultiEdit"] = "file_path",
        ["NotebookEdit"] = "notebook_path"
    };

    private readonly StringBuilder _reply = new();
    private readonly StringBuilder _buffer = new();

    public string Reply => _reply.ToString();
    public string? SessionId { get; private set; }

    /// <summary>The session's model, from the init event; what usage is attributed to when it does not say.</summary>
    private string? _model;

    /// <summary>Whether the CLI reported its own failure, which is not the same as a non-zero exit.</summary>
    public bool Failed { get; private set; }

    public async Task HandleAsync(SandboxOutput output)
    {
        // stderr from the CLI is diagnostics, not content. Logged, never shown: a person asking for a new
        // headline should not see a node warning.
        if (output.IsError)
        {
            logger.LogDebug("claude stderr: {Text}", output.Text);
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
                // A field with an unexpected JSON type — `type` as a number, say. Thrown by GetValue<string>,
                // and skipped for the same reason an unknown event type is: this stream is another product's
                // interface, and one surprising field must not lose a turn's work.
                logger.LogDebug(exception, "Skipped a stream-json line that did not have the expected shape.");
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
            // A partial line, which happens when a chunk boundary lands mid-object and the stream ends.
            return;
        }

        SessionId ??= node?["session_id"]?.GetValue<string>();

        switch (node?["type"]?.GetValue<string>())
        {
            case "assistant":
                foreach (var block in node["message"]?["content"]?.AsArray() ?? [])
                {
                    switch (block?["type"]?.GetValue<string>())
                    {
                        case "text" when block["text"]?.GetValue<string>() is { Length: > 0 } value:
                            _reply.Append(value);
                            await onEvent(new CodingAgentEvent.Text(value));
                            break;

                        case "tool_use":
                            await ReportToolAsync(block);
                            break;

                        // "thinking" falls through on purpose. See the class comment.
                    }
                }

                break;

            case "system" when node["subtype"]?.GetValue<string>() == "init":
                _model = node["model"]?.GetValue<string>();
                break;

            case "result":
                Failed = node["is_error"]?.GetValue<bool>() ?? false;

                // Before anything that can return early: an errored turn was paid for too.
                await ReportUsageAsync(node);

                // The CLI's own final text, which is the authority on what it said — the assistant blocks
                // above include narration the final message drops. Not taken when it errored: there the
                // string is an error report, and showing it as the agent's reply would read as an answer.
                if (!Failed && node["result"]?.GetValue<string>() is { Length: > 0 } final) Replace(final);
                break;
        }
    }

    /// <summary>
    /// What the turn cost, from the <c>result</c> event: <c>total_cost_usd</c>, and tokens from
    /// <c>modelUsage</c> when it is there — one entry per model, because a turn can use more than one — or from
    /// <c>usage</c> when it is not. The model is the one that cost the most, which is the one doing the work.
    ///
    /// <b>Not yet reconciled against a recording</b>: the fixture was sanitised before these fields were kept.
    /// The shape is Claude Code's documented result message; <c>tools/e2e/run.mjs --agent claude</c> now keeps
    /// them, and its next recording settles it. Also unsettled: whether a <c>--resume</c>d session reports its
    /// totals for this invocation or for the whole session. Compare two turns' costs the first time it runs.
    /// </summary>
    private async Task ReportUsageAsync(JsonNode result)
    {
        var cost = Number(result["total_cost_usd"]);
        var models = result["modelUsage"]?.AsObject();
        var usage = result["usage"];

        if (cost == 0 && models is null && usage is null) return;

        AgentUsage reported;

        if (models is { Count: > 0 })
        {
            var main = models.MaxBy(model => Number(model.Value?["costUSD"]));

            reported = new AgentUsage(
                main.Key,
                models.Sum(model => (long)Number(model.Value?["inputTokens"])),
                models.Sum(model => (long)Number(model.Value?["outputTokens"])),
                models.Sum(model => (long)Number(model.Value?["cacheReadInputTokens"])),
                models.Sum(model => (long)Number(model.Value?["cacheCreationInputTokens"])),
                cost);
        }
        else
        {
            reported = new AgentUsage(
                _model,
                (long)Number(usage?["input_tokens"]),
                (long)Number(usage?["output_tokens"]),
                (long)Number(usage?["cache_read_input_tokens"]),
                (long)Number(usage?["cache_creation_input_tokens"]),
                cost);
        }

        await onEvent(new CodingAgentEvent.Usage(reported));
    }

    /// <summary>A JSON number as a decimal, or zero for anything else — this is another product's interface.</summary>
    private static decimal Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : 0m;

    private async Task ReportToolAsync(JsonNode block)
    {
        var tool = block["name"]?.GetValue<string>() ?? "tool";
        var path = WriteTools.TryGetValue(tool, out var key) ? block["input"]?[key]?.GetValue<string>() : null;

        if (path is { Length: > 0 })
            await onEvent(new CodingAgentEvent.FileChanged(Relative(path)));

        await onEvent(new CodingAgentEvent.Activity(Phrase(tool), path is null ? null : Relative(path)));
    }

    /// <summary>
    /// A reported path as the person's site sees it. Absolute paths are what the CLI emits, and the sandbox's
    /// tree is always at <c>/workspace</c> — but the prefix is searched for rather than assumed at position
    /// zero, so a path that arrives in some other shape degrades to itself instead of being truncated wrongly.
    /// </summary>
    private static string Relative(string path)
    {
        var index = path.IndexOf(WorkspacePrefix, StringComparison.Ordinal);

        return index >= 0 ? path[(index + WorkspacePrefix.Length)..] : path.TrimStart('/');
    }

    private void Replace(string text)
    {
        _reply.Clear();
        _reply.Append(text);
    }

    /// <summary>
    /// A tool name as something a person reading it would recognise. The set is the agent's built-in tools;
    /// anything else gets the generic phrase rather than its internal name, because a chip that reads "Glob"
    /// makes the product look like a debugger.
    /// </summary>
    private static string Phrase(string tool) => tool switch
    {
        "Read" => "Reading your site",
        "Write" => "Writing a file",
        "Edit" or "MultiEdit" => "Editing a file",
        "NotebookEdit" => "Editing a file",
        "Glob" or "Grep" => "Looking through your site",
        "Bash" => "Running a command",
        "WebSearch" or "WebFetch" => "Looking something up",
        "TodoWrite" => "Planning",
        "Task" => "Working through it",
        _ => "Working"
    };

    /// <summary>
    /// Splits the reply into what the person sees and the one line Webly commits with. A convention in the
    /// prompt rather than a structured output, because the same convention has to work for a second agent
    /// with a different interface — and a missing line is recoverable: the person's own request is a
    /// perfectly good commit subject.
    ///
    /// The recorded turn followed it, which is the only evidence that matters here.
    /// </summary>
    public CodingAgentOutcome ToOutcome(string fallbackSummary)
    {
        var reply = Reply.Trim();
        var summary = Shorten(fallbackSummary);
        var lines = reply.Split('\n');

        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();

            if (!line.StartsWith("SUMMARY:", StringComparison.OrdinalIgnoreCase)) continue;

            var value = line["SUMMARY:".Length..].Trim();

            if (value.Length > 0 && !value.Equals("none", StringComparison.OrdinalIgnoreCase))
                summary = Shorten(value);

            reply = string.Join('\n', lines.Take(index)).TrimEnd();
            break;
        }

        return new CodingAgentOutcome(reply, summary, Details: null, SessionId);
    }

    /// <summary>A commit subject's length. Git's own convention is 50; 70 is where a history list wraps.</summary>
    private static string Shorten(string value) => value.Length <= 70 ? value : $"{value[..67]}...";
}
