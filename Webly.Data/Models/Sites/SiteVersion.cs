using Webly.Data.Models.Authentication;
using Webly.Data.Models.Chat;
using Webly.Data.Models.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Sites;

/// <summary>
/// One version of a site — which is to say <b>one commit</b> in that site's git repository.
///
/// The repository is the content; this row is the index. Git already does immutable snapshots, parents,
/// diffs and cheap storage far better than a table could, so nothing here duplicates it: no file
/// contents, no tree, no diff. What the row adds is everything git has no opinion about — a nanoid the
/// API can address it by, which chat message asked for it, whether it was a restore, and whether it is
/// the one that is live.
///
/// Immutable, like the commit it names. A restore does not move a pointer backwards: it writes the old
/// commit's tree forward as a new commit, so history stays reachable and undoing an undo is the same
/// operation again.
/// </summary>
public class SiteVersion : IHasNanoid, IHasCreatedAt
{
    [Key]
    public int Id { get; set; }

    public string Nanoid { get; set; } = string.Empty;

    /// <summary>
    /// No <c>UpdatedAt</c>, deliberately — <see cref="IHasTimestamps"/> is not implemented because a
    /// version that can be updated is not a version. Stamped on insert by the context.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    /// <summary>
    /// The commit. Forty hex characters, and the only handle anything needs in order to check out this
    /// exact site: the sandbox seeds a workspace from it, the deployment builds it, the history diffs it.
    /// Unique per site — git guarantees it globally, and the index says so per site because that is the
    /// scope every query has.
    /// </summary>
    public required string CommitSha { get; set; }

    /// <summary>
    /// What this version was derived from, as a row rather than as git's own parent pointer. Both exist,
    /// and they agree: git's parent is the truth about the repository, this is what the history list
    /// walks without shelling out. Null only for a site's first commit.
    /// </summary>
    public int? ParentVersionId { get; set; }
    public SiteVersion? ParentVersion { get; set; }

    /// <summary>
    /// One sentence about what changed, which is also the commit's subject line — written once, by the
    /// agent for a turn it committed or by the use case for a restore. Not optional: a history of
    /// twenty-nine entries called "Update site" is not a history, and neither is a git log of them.
    /// </summary>
    public required string Summary { get; set; }

    /// <summary>
    /// The agent's longer account of the change, when it wrote one — the commit body. Shown when a
    /// history entry is opened, so "why" survives longer than the conversation.
    /// </summary>
    public string? Details { get; set; }

    public SiteVersionOrigin Origin { get; set; }

    /// <summary>
    /// How many files the commit touched. Denormalized from git because the history list shows it on
    /// every row, and running <c>git show --stat</c> per row to draw a list is a process per row.
    /// </summary>
    public int ChangedFileCount { get; set; }

    /// <summary>
    /// Who caused it. Kept even when the change came from the agent — the agent acts for a person, and
    /// this is the person it acted for.
    /// </summary>
    public int CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;

    /// <summary>
    /// The chat message whose turn produced this version, when one did. This is the link that makes the
    /// history read as the conversation that caused it — open a version and you can see the sentence
    /// that asked for it.
    /// </summary>
    public int? SourceMessageId { get; set; }
    public ConversationMessage? SourceMessage { get; set; }

    /// <summary>
    /// The version this one restored, when <see cref="Origin"/> is
    /// <see cref="SiteVersionOrigin.Restore"/>. Makes "restored from Tuesday" renderable without parsing
    /// <see cref="Summary"/>.
    /// </summary>
    public int? RestoredFromVersionId { get; set; }
    public SiteVersion? RestoredFromVersion { get; set; }
}
