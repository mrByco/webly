using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Agent.Agents;

/// <summary>
/// Claude Code, headless, in the sandbox: <c>claude -p --output-format stream-json</c>.
///
/// The CLI rather than an API: it brings the harness this product needs — read, write, edit, glob, grep,
/// bash, its own context management and its own sense of when a task is done — and reimplementing that over
/// a chat API would be rebuilding the tool we chose. What Webly adds around it is everything the CLI has no
/// opinion about: which tree it starts from, what happens to the tree afterwards, and who is allowed to ask.
///
/// Two flags carry most of the design:
///
/// <list type="bullet">
/// <item><c>--permission-mode acceptEdits</c>, because there is nobody at a terminal to approve a file
/// write. What makes that safe is the sandbox, not a prompt: see <see cref="ISandbox"/>.</item>
/// <item><c>--resume</c>, so the second turn in a workspace keeps everything the first learned about the
/// codebase. It is also why <see cref="CodingAgentRequest.History"/> is only sent when there is no session
/// to resume — otherwise the same history is paid for twice.</item>
/// </list>
///
/// <b>Unverified against the CLI.</b> The flags and the <c>stream-json</c> event shapes below follow Claude
/// Code's documented headless interface, but nothing here has been run — see whats_next.md. The parser is
/// written to ignore what it does not recognise for that reason: an unknown event type costs a chip in the
/// UI, not the turn.
/// </summary>
public class ClaudeCodeAgent(
    IOptions<CodingAgentOptions> options,
    ILogger<ClaudeCodeAgent> logger) : ICodingAgent
{
    public const string AgentKey = "claude-code";

    public string Key => AgentKey;

    private readonly CodingAgentOptions _options = options.Value;

    public bool IsConfigured => _options.ClaudeCode.IsConfigured;

    public async Task<CodingAgentOutcome> RunAsync(
        ISandbox sandbox,
        CodingAgentRequest request,
        Func<CodingAgentEvent, Task> onEvent,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "-p", BuildPrompt(request),
            "--output-format", "stream-json",
            // Verbose is required alongside stream-json for the CLI to emit anything but the final result;
            // without it the editor shows nothing for two minutes and then everything at once.
            "--verbose",
            "--permission-mode", "acceptEdits",
            "--model", _options.Model,
            // The site's own instructions are in its repository (AGENTS.md, CLAUDE.md), so the agent reads
            // them as part of the workspace rather than being handed them here. That is what lets facts about
            // a business accumulate for the next session instead of living in one prompt.
            "--add-dir", "/workspace"
        };

        if (request.SessionId is { Length: > 0 } sessionId)
        {
            arguments.Add("--resume");
            arguments.Add(sessionId);
        }

        var command = new SandboxCommand(
            "claude",
            arguments,
            _options.TurnTimeout,
            new Dictionary<string, string>
            {
                ["ANTHROPIC_API_KEY"] = _options.ClaudeCode.ApiKey,
                // The CLI is interactive by default and will try to be clever about a TTY it does not have.
                ["CI"] = "true"
            });

        var parser = new StreamJsonParser(onEvent, logger);

        var result = await sandbox.RunAsync(command, parser.HandleAsync, cancellationToken);

        if (!result.Succeeded && parser.Reply.Length == 0)
            throw new SandboxException(
                "The editing agent could not finish.",
                $"claude exited {result.ExitCode}: {Tail(result.Output)}");

        return parser.ToOutcome(request.Message);
    }

    /// <summary>
    /// What the agent is asked. Short on purpose: the standing instructions live in the site's repository
    /// where both agents read them, and repeating them here would be a second copy to keep in step.
    ///
    /// The one thing this prompt insists on is the summary line, because Webly needs a commit subject and the
    /// history is unreadable without one.
    /// </summary>
    private static string BuildPrompt(CodingAgentRequest request)
    {
        var prompt = new StringBuilder();

        if (request.History.Count > 0)
        {
            prompt.AppendLine("Earlier in this conversation:");

            foreach (var turn in request.History) prompt.AppendLine($"- {turn}");

            prompt.AppendLine();
        }

        prompt.AppendLine(request.Message);
        prompt.AppendLine();
        prompt.AppendLine("""
            When you are done, end your reply with a line of the form:

            SUMMARY: <one sentence, under 70 characters, describing what you changed>

            Write it in the past tense and name what changed, not what you did — it becomes the entry in the
            site owner's history. If you changed nothing, say SUMMARY: none.
            """);

        return prompt.ToString();
    }

    private static string Tail(string output) =>
        output.Length <= 2000 ? output : output[^2000..];

    /// <summary>
    /// Reads the CLI's <c>stream-json</c> output: one JSON object per line, of which this cares about three
    /// kinds — assistant text, tool use, and the final result carrying the session id.
    ///
    /// Tolerant by construction. The stream is a different product's interface, so anything unrecognised is
    /// skipped rather than failing the turn, and the reply is accumulated from whatever text did arrive.
    /// </summary>
    private sealed class StreamJsonParser(Func<CodingAgentEvent, Task> onEvent, ILogger logger)
    {
        private readonly StringBuilder _reply = new();
        private readonly StringBuilder _buffer = new();

        public string Reply => _reply.ToString();
        public string? SessionId { get; private set; }

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

                if (line.Length > 0) await HandleLineAsync(line);
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
                        }
                    }

                    break;

                case "result":
                    // The CLI's own final text, which is the authority on what it said — the assistant blocks
                    // above may have been partial.
                    if (node["result"]?.GetValue<string>() is { Length: > 0 } final) Replace(final);
                    break;
            }
        }

        private async Task ReportToolAsync(JsonNode block)
        {
            var tool = block["name"]?.GetValue<string>() ?? "tool";
            var path = block["input"]?["file_path"]?.GetValue<string>();

            if (path is { Length: > 0 })
            {
                var relative = path.StartsWith("/workspace/", StringComparison.Ordinal) ? path["/workspace/".Length..] : path;

                if (tool is "Write" or "Edit" or "NotebookEdit")
                    await onEvent(new CodingAgentEvent.FileChanged(relative));
            }

            await onEvent(new CodingAgentEvent.Activity(Phrase(tool), path));
        }

        private void Replace(string text)
        {
            _reply.Clear();
            _reply.Append(text);
        }

        /// <summary>
        /// A tool name as something a person reading it would recognise. The set is the agent's built-in
        /// tools; anything else gets the generic phrase rather than its internal name, because a chip that
        /// reads "Glob" makes the product look like a debugger.
        /// </summary>
        private static string Phrase(string tool) => tool switch
        {
            "Read" => "Reading your site",
            "Write" => "Writing a file",
            "Edit" => "Editing a file",
            "Glob" or "Grep" => "Looking through your site",
            "Bash" => "Running a command",
            "WebSearch" or "WebFetch" => "Looking something up",
            "TodoWrite" => "Planning",
            _ => "Working"
        };

        /// <summary>
        /// Splits the reply into what the person sees and the one line Webly commits with. A convention in the
        /// prompt rather than a structured output, because the same convention has to work for a second agent
        /// with a different interface — and a missing line is recoverable: the person's own request is a
        /// perfectly good commit subject.
        /// </summary>
        public CodingAgentOutcome ToOutcome(string fallbackSummary)
        {
            var reply = Reply.Trim();
            var summary = fallbackSummary.Length <= 70 ? fallbackSummary : $"{fallbackSummary[..67]}...";
            var lines = reply.Split('\n');

            for (var index = lines.Length - 1; index >= 0; index--)
            {
                var line = lines[index].Trim();

                if (!line.StartsWith("SUMMARY:", StringComparison.OrdinalIgnoreCase)) continue;

                var value = line["SUMMARY:".Length..].Trim();

                if (value.Length > 0 && !value.Equals("none", StringComparison.OrdinalIgnoreCase))
                    summary = value.Length <= 70 ? value : $"{value[..67]}...";

                reply = string.Join('\n', lines.Take(index)).TrimEnd();
                break;
            }

            return new CodingAgentOutcome(reply, summary, Details: null, SessionId);
        }
    }
}
