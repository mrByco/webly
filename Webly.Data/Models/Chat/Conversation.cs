using Webly.Data.Models.Authentication;
using Webly.Data.Models.Interfaces;
using Webly.Data.Models.Sites;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Chat;

/// <summary>
/// A thread of editing about one site. Scoped to the site rather than the account: the chat <i>is</i>
/// the editor, so its history belongs next to the thing it changed — which is also what lets a version
/// in the history link back to the sentence that asked for it.
///
/// One active thread per site, and "New chat" archives the current one. There is no thread list in the
/// product; the table shape is the one a list would need anyway, so adding it later is a query and a
/// page rather than a migration.
/// </summary>
public class Conversation : IHasNanoid, IHasTimestamps
{
    [Key]
    public int Id { get; set; }

    public string Nanoid { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    /// <summary>
    /// Who is talking. Kept even though a site has one owner, so that the day collaboration lands the
    /// threads do not all read as the owner's.
    /// </summary>
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// A few words, derived from the first message. Written once by the turn that created the thread;
    /// not asked of the model as a separate call, which would be a second provider round trip for a
    /// label nobody reads twice.
    /// </summary>
    public string? Title { get; set; }

    public ConversationStatus Status { get; set; } = ConversationStatus.Active;

    /// <summary>In <see cref="ConversationMessage.Sequence"/> order.</summary>
    public ICollection<ConversationMessage> Messages { get; set; } = [];
}
