using Webly.Data.Models.Authentication;
using Webly.Data.Models.Chat;
using Webly.Data.Models.Deployments;
using Webly.Data.Models.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Sites;

/// <summary>
/// One website — which is to say one <b>Next.js repository</b> that Webly owns on the customer's behalf.
///
/// A site's content is not in this database. It is source code in a bare git repository keyed by
/// <see cref="Nanoid"/> (see <c>ISiteRepositoryStore</c>), and the rows here are the index over it: who
/// owns it, where it is published, and which commits are worth naming. <see cref="HeadVersionId"/> is the
/// tip of the working branch — what the editor and the agent are changing — and
/// <see cref="PublishedVersionId"/> is what the world sees, which moves only when a deployment of that
/// exact commit has succeeded.
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
    /// Deleting the account deletes the sites — a site with no owner has nobody to pay for it and nobody
    /// who may take it down.
    /// </summary>
    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    /// <summary>What the person calls it. Not unique: two drafts of the same idea are normal.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The subdomain the site is reachable at once published (<c>{Slug}.webly.site</c>), globally unique
    /// and assigned at creation. It exists so a site can be published — and shared, and linked from an
    /// email — before anybody has bought a domain, which is the whole self-service premise.
    /// </summary>
    public required string Slug { get; set; }

    /// <summary>
    /// The branch the editor works on. A column rather than a constant because a customer's repository is
    /// theirs and an export could come back with <c>master</c>, but nothing in the product offers a second
    /// branch: there is one line of history, which is what makes "go back" a button rather than a concept.
    /// </summary>
    public string DefaultBranch { get; set; } = "main";

    /// <summary>
    /// The tip of <see cref="DefaultBranch"/>. Never null after creation — creating a site commits the
    /// starter template in the same transaction, so there is no state in which a site exists with nothing
    /// to check out.
    /// </summary>
    public int? HeadVersionId { get; set; }
    public SiteVersion? HeadVersion { get; set; }

    /// <summary>
    /// The version currently serving visitors, or null for a site that has never been published. Written
    /// by the deployment runner only after the provider reports success: a site whose publish failed must
    /// still show its previous version, and this pointer is what guarantees that.
    /// </summary>
    public int? PublishedVersionId { get; set; }
    public SiteVersion? PublishedVersion { get; set; }

    /// <summary>
    /// The provider-side project this site deploys into, created lazily on the first publish. Stored so a
    /// second publish reuses the project — and so an orphaned project can be found when the site is
    /// deleted.
    /// </summary>
    public string? ProviderProjectId { get; set; }

    /// <summary>
    /// When the provider confirmed that <c>{Slug}.{BaseDomain}</c> serves this site's project — in other words,
    /// when the address every screen prints stopped being an assumption.
    ///
    /// It exists because the subdomain is not free: a provider serves a hostname only once that hostname has been
    /// attached to the project, and until then the address is somewhere the site is *not*. The publish path
    /// attaches it and stamps this, so the URL the header links and the email carries can be the one that
    /// resolves — the deployment's own URL while this is null, the site's address once it is not.
    ///
    /// Null is the normal state in development, where the substitute deployment target serves from this host and
    /// nothing anywhere resolves a subdomain of the production zone.
    /// </summary>
    public DateTime? AddressReadyAt { get; set; }

    /// <summary>Named commits, newest first. See <see cref="SiteVersion"/>.</summary>
    public ICollection<SiteVersion> Versions { get; set; } = [];

    public ICollection<Domain> Domains { get; set; } = [];

    public ICollection<Deployment> Deployments { get; set; } = [];

    /// <summary>The editing conversations about this site. Scoped to the site, not the account.</summary>
    public ICollection<Conversation> Conversations { get; set; } = [];
}
