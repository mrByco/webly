namespace Webly.Data.Models.Chat;

/// <summary>
/// Who said a message. Mirrors the provider-neutral roles of <c>Microsoft.Extensions.AI</c> rather
/// than inventing our own, because the hydration path maps these straight onto <c>ChatRole</c>.
/// </summary>
public enum MessageRole
{
    User,
    Assistant,

    /// <summary>
    /// A tool call and its result, kept as one message so the pair cannot be persisted half-written.
    /// These are dropped when history is hydrated for the next turn — see <c>AgentTurnService</c>:
    /// the tool traffic of an edit is large, and stale the moment the document moves on.
    /// </summary>
    Tool,

    /// <summary>
    /// Something the app said, not the model: "Published to example.com", "Restored version 12". In the
    /// thread because the person's picture of what happened to their site should be one list.
    /// </summary>
    System
}
