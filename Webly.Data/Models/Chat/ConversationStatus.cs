namespace Webly.Data.Models.Chat;

public enum ConversationStatus
{
    /// <summary>The thread the editor opens. At most one per site — a partial unique index says so.</summary>
    Active,

    /// <summary>Kept, not shown. "New chat" archives rather than deletes, because the versions it
    /// produced link back to its messages and a deleted thread would leave that history mute.</summary>
    Archived
}
