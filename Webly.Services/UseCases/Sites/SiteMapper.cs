using Microsoft.Extensions.Options;
using Webly.Data.Models.Sites;
using Webly.Services.DTO.Domains;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Deployments;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Entities to DTOs, in one place. Also the one place a site's public URL is decided — from its primary domain if
/// it has a verified one, otherwise from its Webly subdomain — because a second implementation of that rule is a
/// link that points somewhere the site is not.
/// </summary>
public class SiteMapper(IOptions<SitesOptions> sites)
{
    public string UrlFor(Site site, Domain? primaryDomain) =>
        primaryDomain is { VerificationState: DomainVerificationState.Verified }
            ? $"https://{primaryDomain.Hostname}"
            : sites.Value.UrlFor(site.Slug);

    public SiteSummaryResponse ToSummary(Site site, Domain? primaryDomain, DateTime? publishedAt) => new()
    {
        Nanoid = site.Nanoid,
        Name = site.Name,
        Slug = site.Slug,
        Url = UrlFor(site, primaryDomain),
        PublishedAt = publishedAt,
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
