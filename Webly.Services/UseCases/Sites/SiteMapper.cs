using Microsoft.Extensions.Options;
using Webly.Data.Models.Sites;
using Webly.Services.DTO.Domains;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Sites;

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
        HasUnpublishedChanges = site.PublishedVersionId != site.DraftVersionId,
        UpdatedAt = site.UpdatedAt
    };

    public static SiteVersionResponse ToVersion(SiteVersion version, Site site, bool includeDocument) => new()
    {
        Nanoid = version.Nanoid,
        Summary = version.Summary,
        Origin = version.Origin,
        CreatedAt = version.CreatedAt,
        RestoredFromNanoid = version.RestoredFromVersion?.Nanoid,
        IsDraft = site.DraftVersionId == version.Id,
        IsPublished = site.PublishedVersionId == version.Id,
        Document = includeDocument ? version.Document : null
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

    /// <summary>The catalogue, flattened for the client's property editor.</summary>
    public static IReadOnlyList<SectionSchemaResponse> Catalogue() =>
    [
        .. SectionCatalogue.All.Select(schema => new SectionSchemaResponse
        {
            Type = schema.Type.ToString(),
            Label = schema.Label,
            Purpose = schema.Purpose,
            Fields = [.. schema.Fields.Select(ToField)]
        })
    ];

    private static SectionFieldResponse ToField(SectionFieldSchema field) => new()
    {
        Name = field.Name,
        Kind = field.Kind.ToString(),
        Description = field.Description,
        Required = field.Required,
        MaxLength = field.MaxLength,
        MaxItems = field.MaxItems,
        Choices = field.Choices ?? [],
        ItemFields = [.. (field.ItemFields ?? []).Select(ToField)]
    };
}
