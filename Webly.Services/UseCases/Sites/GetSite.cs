using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// The site the editor opens on: its draft document, its published version if any, and its domains.
/// </summary>
public class GetSite(
    ISiteRepository siteRepository,
    ISiteVersionRepository versionRepository,
    IDomainRepository domainRepository,
    SiteMapper mapper)
{
    public async Task<Result<SiteError, SiteDetailResponse>> ExecuteAsync(
        int userId,
        string nanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerAsync(nanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError, SiteDetailResponse>.Fail(SiteError.NotFound);

        var draft = site.DraftVersion
            ?? throw new InvalidOperationException($"Site '{site.Nanoid}' has no draft version.");

        var domains = await domainRepository.ListForSiteAsync(site.Id, cancellationToken);
        var primary = domains.FirstOrDefault(x => x.IsPrimary);

        var published = site.PublishedVersionId is null
            ? null
            : await versionRepository.FindByIdForSiteAsync(site.PublishedVersionId.Value, site.Id, cancellationToken);

        return Result<SiteError, SiteDetailResponse>.Ok(new SiteDetailResponse
        {
            Summary = mapper.ToSummary(site, primary, published?.CreatedAt),
            DraftVersion = SiteMapper.ToVersion(draft, site, includeDocument: true),
            // Without its document: the editor shows the draft, and the published one is only named here so the
            // header can say what is live. Loading a second whole document for a badge would be waste.
            PublishedVersion = published is null ? null : SiteMapper.ToVersion(published, site, includeDocument: false),
            Domains = [.. domains.Select(SiteMapper.ToDomain)]
        });
    }
}
