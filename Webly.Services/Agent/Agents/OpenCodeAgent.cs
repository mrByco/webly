using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Agent.Agents;

/// <summary>
/// OpenCode, headless, in the sandbox: <c>opencode run "…"</c>.
///
/// The second agent, and the reason there is an interface at all: it is open source, self-hostable and
/// provider-agnostic, so it is the answer to "what if we need to change model provider, or run this entirely
/// on our own infrastructure". Keeping it working costs this class.
///
/// It is deliberately the plainer implementation. OpenCode's headless output is text rather than a typed
/// event stream, so the person sees the agent's prose and a coarse activity line instead of per-tool chips —
/// which is the honest consequence of the choice, not something to paper over with guesses about its
/// internal format. If it becomes the default, the upgrade is its server mode (<c>opencode serve</c>) and its
/// event stream.
///
/// <b>Unverified against the CLI</b>, like everything else that talks to another product from here. See
/// whats_next.md.
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
            new Dictionary<string, string>
            {
                ["ANTHROPIC_API_KEY"] = _options.OpenCode.ApiKey,
                ["OPENCODE_DISABLE_TUI"] = "1",
                ["CI"] = "true"
            });

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

        if (!result.Succeeded && reply.Length == 0)
            throw new SandboxException(
                "The editing agent could not finish.",
                $"opencode exited {result.ExitCode}: {result.Output}");

        // The session id is not printed, so the workspace remembers only that there *is* one to continue:
        // --continue takes the last session in the directory, which is exactly what a warm workspace means.
        return Parse(reply.ToString(), request.Message, sessionId: "last");
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
