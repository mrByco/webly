using Microsoft.Extensions.Options;
using Webly.Data.Models.Deployments;
using Webly.Data.Models.Sites;
using Webly.Services.DTO.Domains;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Deployments;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Entities to DTOs, in one place. Also the one place both of a site's URLs are decided, which is the point: a
/// second implementation of either rule is a link that points somewhere the site is not.
///
/// The two are different questions. <see cref="UrlFor"/> is the site's <b>address</b> — its verified primary domain
/// if it has one, otherwise its Webly subdomain — and it is what the settings screen prints and what a person reads
/// out over the phone. <see cref="LiveUrlFor"/> is where the published version is <b>actually being served right
/// now</b>, which is the address only once the provider has been told to serve that hostname, and the deployment's
/// own provider URL until then. A header that linked the address unconditionally is the "live according to us, gone
/// according to the internet" mistake the domains code goes out of its way to avoid.
/// </summary>
public class SiteMapper(IOptions<SitesOptions> sites)
{
    public string UrlFor(Site site, Domain? primaryDomain) =>
        primaryDomain is { VerificationState: DomainVerificationState.Verified }
            ? $"https://{primaryDomain.Hostname}"
            : sites.Value.UrlFor(site.Slug);

    /// <summary>
    /// Where the site can be opened, or null for one that has never been published. Three tiers, in the order of
    /// how much we know: a verified custom domain is being served by the provider because attaching it is what
    /// verifying it means; the Webly subdomain once <see cref="Site.AddressReadyAt"/> says the provider accepted
    /// it; and otherwise the deployment's own URL, which is the one thing a provider always serves.
    /// </summary>
    public string? LiveUrlFor(Site site, Domain? primaryDomain, Deployment? liveDeployment)
    {
        if (site.PublishedVersionId is null) return null;

        if (primaryDomain is { VerificationState: DomainVerificationState.Verified })
            return $"https://{primaryDomain.Hostname}";

        return site.AddressReadyAt is not null
            ? sites.Value.UrlFor(site.Slug)
            : liveDeployment?.ProviderUrl;
    }

    public SiteSummaryResponse ToSummary(
        Site site,
        Domain? primaryDomain,
        DateTime? publishedAt,
        Deployment? liveDeployment = null) => new()
    {
        Nanoid = site.Nanoid,
        Name = site.Name,
        Slug = site.Slug,
        WeblyUrl = sites.Value.UrlFor(site.Slug),
        Url = UrlFor(site, primaryDomain),
        LiveUrl = LiveUrlFor(site, primaryDomain, liveDeployment),
        PublishedAt = publishedAt,
        AddressReadyAt = site.AddressReadyAt,
        // Two pointers, one question. Equal means the world is looking at what the editor is editing.
        HasUnpublishedChanges = site.PublishedVersionId != site.HeadVersionId,
        UpdatedAt = site.UpdatedAt
    };

    public static SiteVersionResponse ToVersion(SiteVersion version, Site site) => new()
    {
        Nanoid = version.Nanoid,
        CommitSha = version.CommitSha,
        Summary = version.Summary,
        Details = version.Details,
        Origin = version.Origin,
        ChangedFileCount = version.ChangedFileCount,
        CreatedAt = version.CreatedAt,
        RestoredFromNanoid = version.RestoredFromVersion?.Nanoid,
        IsHead = site.HeadVersionId == version.Id,
        IsPublished = site.PublishedVersionId == version.Id
    };

    public static DomainResponse ToDomain(Domain domain) => new()
    {
        Nanoid = domain.Nanoid,
        Hostname = domain.Hostname,
        VerificationState = domain.VerificationState,
        IsPrimary = domain.IsPrimary,
        DnsRecordType = domain.DnsRecordType,
        DnsRecordName = domain.DnsRecordName,
        DnsRecordValue = domain.DnsRecordValue,
        VerifiedAt = domain.VerifiedAt,
        LastError = domain.LastError
    };
}
