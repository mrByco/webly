namespace Webly.Data.Models.Chat;

/// <summary>
/// Who said a message. The conventional four rather than a set of our own, because they are what every
/// transcript format and every agent CLI already means by a role — there is no model SDK in this solution
/// to match (see <c>docs/agent-plan.md</c> §1), only a vocabulary worth not reinventing.
/// </summary>
public enum MessageRole
{
    User,
    Assistant,

    /// <summary>
    /// A tool call and its result, kept as one message so the pair cannot be persisted half-written.
    /// <b>Nothing writes one today</b>, deliberately: the agent's tool traffic happens inside a sandbox and
    /// is summarised into the run's events as it goes, and a turn's worth of file writes is both large and
    /// stale the moment the tree moves on. The value exists because a transcript that records tool calls is
    /// a feature this shape already allows, not because it is unused space.
    /// </summary>
    Tool,

    /// <summary>
    /// Something the app said, not the model: "Published to example.com", "Restored version 12". In the
    /// thread because the person's picture of what happened to their site should be one list.
    /// </summary>
    System
}
