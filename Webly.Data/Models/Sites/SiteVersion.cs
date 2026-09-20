using Webly.Data.Models.Authentication;
using Webly.Data.Models.Chat;
using Webly.Data.Models.Interfaces;
using Webly.Data.Models.Sites.Document;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Sites;

/// <summary>
/// A complete, immutable snapshot of a site at one moment, and the unit of versioning the product
/// sells. Nothing updates a version: every accepted change appends a new one whose
/// <see cref="ParentVersionId"/> points at what it was derived from, so the chain is append-only and
/// history can never be rewritten — including by a restore, which copies an old document forward as
/// a new version rather than moving a pointer backwards over the versions in between.
///
/// A whole document per version rather than a diff: a diff chain has to be replayed before anything
/// can be rendered, one bad entry poisons everything after it, and the thing a person wants to see
/// ("what did my site look like on Tuesday") is exactly the snapshot. A document is a few kilobytes
/// of jsonb; a site with a thousand versions is still smaller than one of its hero images.
/// </summary>
public class SiteVersion : IHasNanoid
{
    [Key]
    public int Id { get; set; }

    public string Nanoid { get; set; } = string.Empty;

    /// <summary>
    /// No <c>UpdatedAt</c>, deliberately — <see cref="IHasTimestamps"/> is not implemented because a
    /// version that can be updated is not a version. Stamped by hand on insert.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    /// <summary>
    /// What this version was derived from. Null only for a site's first version. Not a foreign key
    /// that cascades: deleting a version is not a thing that happens, and the site's own delete
    /// cascade takes the whole chain at once.
    /// </summary>
    public int? ParentVersionId { get; set; }
    public SiteVersion? ParentVersion { get; set; }

    /// <summary>
    /// The site, in full: pages, sections, theme and SEO. Serialized to a jsonb column by a value
    /// converter (see <c>WeblyDbContext</c>) rather than mapped as owned entities, because the
    /// document is read and written whole and its section props are deliberately open-ended per
    /// section type.
    /// </summary>
    public required SiteDocument Document { get; set; }

    /// <summary>
    /// One sentence about what changed, shown in the history list. Written by the agent for a turn it
    /// committed, by the use case for a manual edit or a restore. Not optional: a history of
    /// twenty-nine entries called "Updated site" is not a history.
    /// </summary>
    public required string Summary { get; set; }

    public SiteVersionOrigin Origin { get; set; }

    /// <summary>
    /// Who caused it. Kept even when the change came from the agent — the agent acts for a person,
    /// and this is the person it acted for.
    /// </summary>
    public int CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;

    /// <summary>
    /// The chat message whose turn produced this version, when one did. This is the link that makes
    /// the history read as the conversation that caused it — open a version and you can see the
    /// sentence that asked for it. Set null when the conversation is deleted rather than taking the
    /// version with it: the site's history outlives the chat about it.
    /// </summary>
    public int? SourceMessageId { get; set; }
    public ConversationMessage? SourceMessage { get; set; }

    /// <summary>
    /// The version this one restored, when <see cref="Origin"/> is
    /// <see cref="SiteVersionOrigin.Restore"/>. Makes "restored from Tuesday" renderable without
    /// parsing <see cref="Summary"/>.
    /// </summary>
    public int? RestoredFromVersionId { get; set; }
    public SiteVersion? RestoredFromVersion { get; set; }
}
