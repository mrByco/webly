using Webly.Data.Models.Authentication;
using Webly.Data.Models.Chat;
using Webly.Data.Models.Deployments;
using Webly.Data.Models.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Sites;

/// <summary>
/// One website. A site owns no content of its own: everything a visitor would see lives in a
/// <see cref="SiteVersion"/>, and the site is the identity plus two pointers into its version chain.
///
/// <see cref="DraftVersionId"/> is what the editor and the agent work on; <see cref="PublishedVersionId"/>
/// is what the world sees, and it moves only when a deployment of that version has actually
/// succeeded. Two pointers rather than a mutable draft column and a published snapshot column,
/// because a snapshot beside a pointer is two representations of one fact that can disagree.
/// </summary>
public class Site : IHasNanoid, IHasTimestamps
{
    [Key]
    public int Id { get; set; }

    public string Nanoid { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// The one owner. There are no memberships and no roles: see CLAUDE.md "Sites and ownership".
    /// Deleting the account deletes the sites — a site with no owner has nobody to pay for it and
    /// nobody who may take it down.
    /// </summary>
    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    /// <summary>What the person calls it. Not unique: two drafts of the same idea are normal.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The subdomain the site is always reachable at (<c>{Slug}.webly.site</c>), globally unique and
    /// assigned at creation. It exists so that a site can be published — and shared, and linked from
    /// an email — before anybody has bought a domain, which is the whole self-service premise.
    /// A custom domain is added alongside it, never instead of it: <see cref="Domains"/>.
    /// </summary>
    public required string Slug { get; set; }

    /// <summary>
    /// The version the editor is open on. Never null after creation — creating a site writes its
    /// first version from a starter template in the same transaction, so there is no state in which
    /// a site exists with nothing to render.
    /// </summary>
    public int? DraftVersionId { get; set; }
    public SiteVersion? DraftVersion { get; set; }

    /// <summary>
    /// The version currently serving visitors, or null for a site that has never been published.
    /// Written by <c>PublishSite</c> only after the deployment reports success: a site whose publish
    /// failed must still show its previous version, and this pointer is what guarantees that.
    /// </summary>
    public int? PublishedVersionId { get; set; }
    public SiteVersion? PublishedVersion { get; set; }

    /// <summary>
    /// The provider-side project this site deploys into, created lazily on the first publish. Stored
    /// so a second publish reuses the project rather than creating a new one — and so an orphaned
    /// project can be found and removed when the site is deleted.
    /// </summary>
    public string? ProviderProjectId { get; set; }

    /// <summary>Immutable history, oldest first. See <see cref="SiteVersion"/>.</summary>
    public ICollection<SiteVersion> Versions { get; set; } = [];

    public ICollection<Domain> Domains { get; set; } = [];

    public ICollection<Deployment> Deployments { get; set; } = [];

    /// <summary>The editor chats about this site. Scoped to the site, not the account.</summary>
    public ICollection<Conversation> Conversations { get; set; } = [];
}
