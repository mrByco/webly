using Webly.Data.Models.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Sites;

/// <summary>
/// A custom hostname pointing at a site. Webly does not run DNS and does not issue certificates: the
/// deployment provider does both, so this row is a <b>mirror</b> of what the provider knows plus the
/// link to our site. <see cref="VerificationState"/> is copied from the provider on every check and is
/// never decided here — a local "verified" flag that the provider disagrees with is a site that is
/// live according to us and 404 according to the internet.
///
/// The instructions are stored rather than recomputed because they are what the person is looking at
/// while they edit their registrar's DNS panel, and the panel is open in another tab for a quarter of
/// an hour. Recomputing them per request would be one provider API call per page load of a screen
/// people leave open.
/// </summary>
public class Domain : IHasNanoid, IHasTimestamps
{
    [Key]
    public int Id { get; set; }

    public string Nanoid { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    /// <summary>
    /// The hostname, lowercase, no scheme and no trailing dot. Globally unique across Webly: two
    /// sites claiming one hostname is a race the provider would resolve arbitrarily, and losing it
    /// silently is worse than being told the name is taken.
    /// </summary>
    public required string Hostname { get; set; }

    public DomainVerificationState VerificationState { get; set; } = DomainVerificationState.Pending;

    /// <summary>
    /// Which hostname the site canonicalizes to — the one in <c>&lt;link rel="canonical"&gt;</c>, and
    /// the one every other hostname redirects to. Exactly one per site, maintained by
    /// <c>SetPrimaryDomain</c>; the <c>{slug}.webly.site</c> subdomain is primary until a custom domain
    /// is verified and promoted. Two primaries would split a site's search ranking in half.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>The provider's own id for this domain, needed to re-check or remove it.</summary>
    public string? ProviderDomainId { get; set; }

    /// <summary>
    /// What to put in the registrar's DNS panel, as the provider stated it: an <c>A</c> record for an
    /// apex domain, a <c>CNAME</c> for a subdomain, plus a <c>TXT</c> record when the provider asks
    /// for proof of ownership. Stored verbatim, in the provider's own wording, so the screen cannot
    /// tell the person something the provider will not accept.
    /// </summary>
    public string? DnsRecordType { get; set; }
    public string? DnsRecordName { get; set; }
    public string? DnsRecordValue { get; set; }

    /// <summary>
    /// When the provider last confirmed the records. Null while pending. A timestamp rather than a
    /// bool, like <c>User.EmailVerifiedAt</c>: it answers "when", and a domain that stops resolving is
    /// a state worth being able to describe.
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>The provider's explanation when verification failed, shown as-is.</summary>
    public string? LastError { get; set; }
}
