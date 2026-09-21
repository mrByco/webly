using Webly.Data.Models.Interfaces;
using Webly.Data.Models.Sites;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace Webly.Data.Models.Chat;

/// <summary>
/// One message in a thread. Text is the rendered content; <see cref="Parts"/> carries the structured
/// extras (tool calls, their results, a question the agent asked and the answer it got) as jsonb, so
/// reloading the editor reconstructs the same stream the person watched arrive.
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

    /// <summary>Structured content beyond the text. Null for a plain message.</summary>
    public JsonArray? Parts { get; set; }

    /// <summary>
    /// The version this turn committed, if it changed anything. The inverse of
    /// <c>SiteVersion.SourceMessageId</c>, and what lets the chat show "12 changes" inline with a link
    /// to the diff. Null for a turn that only answered a question.
    /// </summary>
    public int? ProducedVersionId { get; set; }
    public SiteVersion? ProducedVersion { get; set; }
}
