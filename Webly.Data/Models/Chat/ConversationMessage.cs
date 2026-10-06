using Webly.Data.Models.Interfaces;
using Webly.Data.Models.Sites;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Chat;

/// <summary>
/// One message in a thread: who said it and what they said, and for a reply, the version it produced.
///
/// <b>Nothing of the turn's activity is kept</b> — the files it touched, the lines it ran — and that is the decision
/// rather than a gap. There was a <c>Parts</c> column for it, documented as what let a reload redraw the stream the
/// person watched arrive, and nothing ever wrote to it or read it. A reloaded thread is the plainer form of a turn:
/// the reply, and a link to the version, whose diff says what the activity lines only gestured at.
/// </summary>
public class ConversationMessage : IHasNanoid, IHasCreatedAt
{
    [Key]
    public int Id { get; set; }

    public string Nanoid { get; set; } = string.Empty;

    /// <summary>
    /// No <c>UpdatedAt</c>: a message is written once. A streamed assistant message is persisted when
    /// the turn finishes, not incrementally — the live stream is what the client watches, and
    /// <c>RunHandle</c>'s replay log is what a reconnect reads, so the row has no job until the end.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    public int ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public MessageRole Role { get; set; }

    /// <summary>
    /// Monotonic within the thread, assigned by the writer. Not <see cref="CreatedAt"/>: two messages
    /// of one turn are saved in the same batch and therefore carry the same timestamp to the
    /// microsecond, so ordering on it is undefined exactly where it matters.
    /// </summary>
    public int Sequence { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The version this turn committed, if it changed anything. The inverse of
    /// <c>SiteVersion.SourceMessageId</c>, and what lets the chat show "12 changes" inline with a link
    /// to the diff. Null for a turn that only answered a question.
    /// </summary>
    public int? ProducedVersionId { get; set; }
    public SiteVersion? ProducedVersion { get; set; }
}
