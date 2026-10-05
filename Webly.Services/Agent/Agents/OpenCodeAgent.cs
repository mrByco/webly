using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
/// It reads <c>--format json</c> (<see cref="OpenCodeJsonParser"/>): what each step cost, the final answer apart
/// from the narration before it, the tools as they are used, and a session id to resume. Text mode, which this
/// was first written against, had none of those — its reply was every "I'm running the check now" along with the
/// answer, and its cost was nowhere.
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
        var arguments = new List<string>
        {
            "run", TurnPrompt.Build(request),
            "--model", _options.OpenCode.Model,
            // Typed events rather than prose: the cost of every step, the final answer apart from the narration
            // before it, the tools as they are used, and a session id to resume. See OpenCodeJsonParser.
            "--format", "json"
        };

        // Its own session, by id — the same trade as Claude Code's --resume, for the same reason.
        if (request.SessionId is { Length: > 0 } sessionId)
        {
            arguments.Add("--session");
            arguments.Add(sessionId);
        }

        var command = new SandboxCommand(
            "opencode",
            arguments,
            _options.TurnTimeout,
            EnvironmentFor(_options.OpenCode));

        var parser = new OpenCodeJsonParser(_options.OpenCode.Model, onEvent, logger);

        var result = await sandbox.RunAsync(command, parser.HandleAsync, cancellationToken);

        // Any failed exit, not only a silent one: a run that dies halfway has already said something, which is
        // what an OpenAI account running out of credit mid-turn looked like — three "successful" turns, each
        // committing half a website under the owner's own message. What it spent before dying has already been
        // reported, step by step, so the turn records it either way.
        if (!result.Succeeded)
            throw new SandboxException(
                "The editing agent could not finish.",
                $"opencode exited {result.ExitCode}: {parser.Error ?? result.Output}");

        return Parse(parser.Reply, request.Message, parser.SessionId);
    }

    /// <summary>
    /// The CLI's environment, with the key handed to whichever provider the model string names, and the browser
    /// tools configured beside it.
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
            },
            // The browser tools (BrowserTools). Here rather than in an opencode.json, which would have to be in the
            // workspace — that is, in the site, committed, and the agent's to edit.
            ["mcp"] = new JsonObject
            {
                [BrowserTools.ServerName] = new JsonObject
                {
                    ["type"] = "local",
                    ["command"] = new JsonArray(BrowserTools.Command)
                }
            }
        };

        return new Dictionary<string, string>
        {
            ["OPENCODE_CONFIG_CONTENT"] = config.ToJsonString(),
            ["OPENCODE_DISABLE_TUI"] = "1",
            ["CI"] = "true"
        };
    }

    /// <summary>The reply and its summary line, split the way every agent's are — see <see cref="TurnPrompt"/>.</summary>
    private static CodingAgentOutcome Parse(string output, string fallbackSummary, string? sessionId)
    {
        var (reply, summary) = TurnPrompt.Split(output, fallbackSummary);

        return new CodingAgentOutcome(reply, summary, Details: null, sessionId);
    }
}
