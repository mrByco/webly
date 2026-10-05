using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json.Nodes;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Agent.Agents;

/// <summary>
/// OpenCode, headless, in the sandbox: <c>opencode run "…"</c>.
///
/// The second agent, and the reason there is an interface at all: it is open source, self-hostable and
/// provider-agnostic, so it is the answer to "what if we need to change model provider, or run this entirely
/// on our own infrastructure". Keeping it working costs this class.
///
/// It is deliberately the plainer implementation. It reads the default text output, where the agent's prose
/// is stdout and its tool calls are stderr, so the person sees the prose and a coarse activity line instead
/// of per-tool chips. The upgrade, if it becomes the default, is <c>--format json</c>: the same command
/// emits typed <c>text</c> and <c>tool_use</c> events carrying a <c>sessionID</c>, which would give it chips
/// and a real session to resume instead of "the last one in this directory".
///
/// <b>Run against opencode 1.18.31 with <c>openai/gpt-5.5</c></b>, which is what found the key going to the
/// wrong place (see <see cref="EnvironmentFor"/>). Two more facts from that run worth not re-deriving: it
/// followed the <c>SUMMARY:</c> line and <c>AGENTS.md</c> unprompted — facts into <c>content/brand.md</c>,
/// the name into <c>siteName</c>, <c>npm run typecheck</c> before finishing — and it <b>waits for stdin to
/// close before doing anything</b>, so a command started with an open stdin hangs silently until the turn
/// times out. The sandbox agent starts every command with stdin ignored, which is what makes that a non-issue
/// here rather than a fifteen-minute turn that says nothing.
/// </summary>
public class OpenCodeAgent(
    IOptions<CodingAgentOptions> options,
    ILogger<OpenCodeAgent> logger) : ICodingAgent
{
    public const string AgentKey = "opencode";

    public string Key => AgentKey;

    private readonly CodingAgentOptions _options = options.Value;

    public bool IsConfigured => _options.OpenCode.IsConfigured;

    public async Task<CodingAgentOutcome> RunAsync(
        ISandbox sandbox,
        CodingAgentRequest request,
        Func<CodingAgentEvent, Task> onEvent,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "run", BuildPrompt(request), "--model", _options.OpenCode.Model };

        // OpenCode keeps its own sessions per directory; continuing the last one in this workspace is the
        // same trade as Claude Code's --resume, for the same reason.
        if (request.SessionId is { Length: > 0 }) arguments.Add("--continue");

        var command = new SandboxCommand(
            "opencode",
            arguments,
            _options.TurnTimeout,
            EnvironmentFor(_options.OpenCode));

        var reply = new StringBuilder();
        var reported = false;

        var result = await sandbox.RunAsync(command, async output =>
        {
            if (output.IsError)
            {
                logger.LogDebug("opencode stderr: {Text}", output.Text);
                return;
            }

            reply.Append(output.Text);

            // One activity line rather than a stream of chips: without a typed event stream, any finer
            // reporting would be a guess at its log format, and a wrong guess reads as a bug.
            if (!reported)
            {
                reported = true;
                await onEvent(new CodingAgentEvent.Activity("Editing your site"));
            }

            await onEvent(new CodingAgentEvent.Text(output.Text));
        }, cancellationToken);

        // Any failed exit, not only a silent one. Text mode has no typed error event, so the exit code is the only
        // signal — and it used to be ignored whenever some prose had come out first, which is what a run that dies
        // halfway looks like: an OpenAI account that ran out of credit mid-turn produced three "successful" turns,
        // each committing half a website under the owner's own message, with a reply that stopped mid-sentence.
        if (!result.Succeeded)
            throw new SandboxException(
                "The editing agent could not finish.",
                $"opencode exited {result.ExitCode}: {result.Output}");

        // The session id is not printed, so the workspace remembers only that there *is* one to continue:
        // --continue takes the last session in the directory, which is exactly what a warm workspace means.
        return Parse(reply.ToString(), request.Message, sessionId: "last");
    }

    /// <summary>
    /// The CLI's environment, with the key handed to whichever provider the model string names.
    ///
    /// It used to be <c>ANTHROPIC_API_KEY</c> whatever the model, which is the one shape this agent exists to
    /// avoid: an OpenAI key under that name and <c>openai/…</c> as the model exits 1 with "Unexpected server
    /// error" and nothing about a key. Inline config rather than a table of variable names, because OpenCode
    /// reads every provider's key from the same place there, while the variable names are each provider's own
    /// (<c>GOOGLE_GENERATIVE_AI_API_KEY</c> is not <c>GOOGLE_API_KEY</c>), and a table would be one more thing
    /// to keep in step with somebody else's product.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> EnvironmentFor(CodingAgentOptions.OpenCodeOptions options)
    {
        var separator = options.Model.IndexOf('/');

        if (separator <= 0)
            throw new InvalidOperationException(
                $"Agent:OpenCode:Model must name its provider, as in 'openai/gpt-5.5'; it is '{options.Model}'.");

        var provider = options.Model[..separator];
        var config = new JsonObject
        {
            ["provider"] = new JsonObject
            {
                [provider] = new JsonObject { ["options"] = new JsonObject { ["apiKey"] = options.ApiKey } }
            }
        };

        return new Dictionary<string, string>
        {
            ["OPENCODE_CONFIG_CONTENT"] = config.ToJsonString(),
            ["OPENCODE_DISABLE_TUI"] = "1",
            ["CI"] = "true"
        };
    }

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
        prompt.AppendLine(
            "End your reply with a line of the form 'SUMMARY: <one past-tense sentence, under 70 characters, "
            + "naming what changed>' — it becomes the entry in the site owner's history. If you changed "
            + "nothing, say SUMMARY: none.");

        return prompt.ToString();
    }

    /// <summary>
    /// The same summary convention as the other agent, parsed the same way — which is the point of putting it
    /// in the prompt rather than in a structured output only one of them supports.
    /// </summary>
    private static CodingAgentOutcome Parse(string output, string fallbackSummary, string sessionId)
    {
        var reply = output.Trim();
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

        return new CodingAgentOutcome(reply, summary, Details: null, sessionId);
    }
}
