namespace Webly.Services.DTO.Realtime;

/// <summary>
/// Everything the server pushes, as one shape. One event type with a discriminator rather than a method per
/// kind of update: the client's dispatch stays a switch, and adding an event never adds a hub method or a
/// client subscription.
/// </summary>
public enum RunEventType
{
    /// <summary>A chunk of the assistant's answer. Coalesced by <c>ChatRunWriter</c>, never one per token.</summary>
    TextDelta,

    /// <summary>The assistant's message is complete. <c>Text</c> is the whole of it.</summary>
    MessageCompleted,

    /// <summary>A tool is being called. <c>Tool</c> names it, <c>Detail</c> is a human phrase for the chip.</summary>
    ToolCall,

    /// <summary>A tool answered. <c>Detail</c> is what to show; failures are results too, not errors.</summary>
    ToolResult,

    /// <summary>The agent is asking the person something and the run is now waiting. See <c>QuestionToolkit</c>.</summary>
    QuestionAsked,

    /// <summary>The question was answered — emitted so a replay shows it resolved rather than pending.</summary>
    QuestionAnswered,

    /// <summary>
    /// The turn committed a new version. Carries the version nanoid, so the editor can refresh the preview
    /// and the history without polling.
    /// </summary>
    VersionCommitted,

    /// <summary>A deployment changed status. <c>Detail</c> is the status name.</summary>
    DeploymentProgress,

    /// <summary>
    /// The run is over, one way or another. Exactly one terminal event per run, emitted on every exit path by
    /// the launcher and by nothing else — a client that never sees one waits forever.
    /// </summary>
    Completed,

    /// <summary>Also terminal. <c>Error</c> says what happened, in words meant for the person.</summary>
    Failed
}

/// <summary>One event, before it is given a sequence number.</summary>
public record RunEvent
{
    public required RunEventType Type { get; init; }

    public string? Text { get; init; }
    public string? Tool { get; init; }
    public string? Detail { get; init; }
    public string? Error { get; init; }

    /// <summary>Set on <see cref="RunEventType.VersionCommitted"/>.</summary>
    public string? VersionNanoid { get; init; }

    /// <summary>Set on the question events, so an answer can be routed back to the tool that is waiting.</summary>
    public string? QuestionId { get; init; }

    /// <summary>The options offered with a question, when it has any.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// An event as it reaches a client: the run it belongs to and a monotonic sequence number.
///
/// <see cref="Seq"/> does two jobs at once, which is why it is on every envelope. It is the resume point — a
/// client that dropped at 41 resubscribes from 41 — and it is the duplicate filter, which is what makes the
/// "join the group, then replay" order in the hub safe.
/// </summary>
public record RunEventEnvelope(RunKind RunKind, string RunId, long Seq, RunEvent Event, bool IsTerminal);
