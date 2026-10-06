using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
/// <b>Verified against the real CLI</b> (2.1.x) by <c>tools/e2e/run.mjs</c>, which runs a turn on the starter
/// template inside a real sandbox and records the stream. Two things that recording settled, because both
/// were guesses before it: the CLI does follow the <c>SUMMARY:</c> convention this prompt asks for, and it
/// edits <c>content/brand.md</c> unprompted because <c>AGENTS.md</c> tells it to — which is the mechanism
/// the product's memory depends on. <see cref="ClaudeStreamJsonParser"/> documents the event shapes.
///
/// What is still unverified is this class under .NET: the process launch, the flag list as
/// <see cref="SandboxCommand"/> passes it, and the timeout. The harness runs the same argv from node.
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
            "-p", TurnPrompt.Build(request),
            "--output-format", "stream-json",
            // Verbose is required alongside stream-json for the CLI to emit anything but the final result;
            // without it the editor shows nothing for two minutes and then everything at once.
            "--verbose",
            "--permission-mode", "acceptEdits",
            // The browser tools (BrowserTools), and no MCP server from anywhere else: a site's repository is
            // written by the agent, and a server it declared there is not one Webly chose.
            "--mcp-config", ClaudeMcpConfig,
            "--strict-mcp-config",
            // The two commands AGENTS.md asks for and the browser tools, and nothing else. acceptEdits lets it write
            // files; a headless run refuses every other shell command and every MCP tool, so without this the
            // typecheck it has always been told to run was refused, and so would be the screenshot it looks at its
            // work with. Named rather than all of Bash, so that an unconfined local sandbox still is not a shell for
            // the model — which is also why webly-browser-mcp withholds the one browser tool that would be.
            "--allowedTools", $"Bash(npm run typecheck),Bash(webly-screenshot *),mcp__{BrowserTools.ServerName}",
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
            EnvironmentFor(_options.ClaudeCode));

        var parser = new ClaudeStreamJsonParser(onEvent, logger);

        var result = await sandbox.RunAsync(command, parser.HandleAsync, cancellationToken);

        // Two ways a turn fails, and they are not the same thing. The CLI can report its own failure in the
        // result event while still exiting zero (`is_error`), and it can exit non-zero having already said
        // something useful. Either is a failed turn; what is not a failure is a non-zero exit with a reply,
        // which is the shape of "it finished and then something tidying up went wrong".
        if (parser.Failed || (!result.Succeeded && parser.Reply.Length == 0))
            throw new SandboxException(
                "The editing agent could not finish.",
                $"claude exited {result.ExitCode}: {Tail(result.Output)}");

        return parser.ToOutcome(request.Message);
    }

    /// <summary>
    /// The CLI's environment: exactly one credential, the API key when there is one and the subscription token
    /// otherwise (see <see cref="CodingAgentOptions.ClaudeCodeOptions.OAuthToken"/>).
    ///
    /// The other one is set <b>empty</b> rather than left out, because a command inherits its sandbox's environment —
    /// and an unconfined local sandbox's is the developer's own, where an <c>ANTHROPIC_API_KEY</c> exported for some
    /// other project would quietly take precedence over the subscription this setting asked for.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> EnvironmentFor(CodingAgentOptions.ClaudeCodeOptions options)
    {
        var apiKey = !string.IsNullOrWhiteSpace(options.ApiKey);

        return new Dictionary<string, string>
        {
            ["ANTHROPIC_API_KEY"] = apiKey ? options.ApiKey : string.Empty,
            ["CLAUDE_CODE_OAUTH_TOKEN"] = apiKey ? string.Empty : options.OAuthToken,
            // The CLI is interactive by default and will try to be clever about a TTY it does not have.
            ["CI"] = "true"
        };
    }

    private static readonly string ClaudeMcpConfig = new JsonObject
    {
        ["mcpServers"] = new JsonObject
        {
            [BrowserTools.ServerName] = new JsonObject { ["type"] = "stdio", ["command"] = BrowserTools.Command }
        }
    }.ToJsonString();

    private static string Tail(string output) =>
        output.Length <= 2000 ? output : output[^2000..];
}
