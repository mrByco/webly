using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Agent;

/// <summary>What a turn tells the agent.</summary>
/// <param name="Message">What the person just asked for.</param>
/// <param name="History">
/// Earlier turns, oldest first, as plain text. Only sent when there is no <paramref name="SessionId"/> to
/// resume: a coding agent that continues its own session already has the history, and sending it again
/// would pay for it twice.
/// </param>
/// <param name="SessionId">
/// The agent's own session in this workspace, if it has one. Resuming keeps everything it learned about the
/// codebase in the last turn — which is most of what makes the second request in a workspace cheap and
/// accurate.
/// </param>
public record CodingAgentRequest(string Message, IReadOnlyList<string> History, string? SessionId);

/// <summary>What the agent did, as the turn needs to record it.</summary>
/// <param name="Reply">What to show the person. The agent's own words, not a log.</param>
/// <param name="Summary">One line for the commit subject and the history list.</param>
/// <param name="Details">The longer account, for the commit body. Null when there was nothing to add.</param>
/// <param name="SessionId">The session to resume next turn, when the agent has one.</param>
public record CodingAgentOutcome(string Reply, string Summary, string? Details, string? SessionId);

/// <summary>
/// Something worth showing while the agent works. Deliberately coarse: the editor shows a sentence and a
/// row of chips, not a debugger.
/// </summary>
public abstract record CodingAgentEvent
{
    /// <summary>A piece of the agent's own prose, as it is written.</summary>
    public record Text(string Value) : CodingAgentEvent;

    /// <summary>The agent used a tool. <paramref name="Phrase"/> is already in the person's language.</summary>
    public record Activity(string Phrase, string? Detail = null) : CodingAgentEvent;

    /// <summary>A file was written. The editor lists these under the turn.</summary>
    public record FileChanged(string Path) : CodingAgentEvent;
}

/// <summary>
/// A coding agent that edits a site's source in a sandbox.
///
/// Two implementations, chosen per request, because the choice is a real one: one is the strongest at
/// long-horizon tool use on a real codebase, the other is open source and provider-agnostic. Keeping both
/// behind this interface costs a class each and is what makes the decision reversible — the orchestration
/// around it (workspace, commit, version, publish) does not know which one ran.
///
/// Implementations run the agent's <b>CLI inside the sandbox</b> rather than calling a model API from here.
/// That is the whole point of the sandbox: the agent needs a filesystem, a shell and a dev server to be any
/// good at this, and reimplementing its harness over a chat API would be rebuilding the product we are
/// using.
/// </summary>
public interface ICodingAgent
{
    /// <summary>The key configuration and requests select it by: <c>claude-code</c>, <c>opencode</c>.</summary>
    string Key { get; }

    /// <summary>Whether this deployment has what this agent needs — an API key, usually.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Runs one turn to completion. Streams events as they happen; returns what the turn produced. Does not
    /// commit anything: the caller reads the workspace afterwards and decides what the change was, which is
    /// what keeps "the agent cannot rewrite history" true.
    /// </summary>
    Task<CodingAgentOutcome> RunAsync(
        ISandbox sandbox,
        CodingAgentRequest request,
        Func<CodingAgentEvent, Task> onEvent,
        CancellationToken cancellationToken = default);
}
