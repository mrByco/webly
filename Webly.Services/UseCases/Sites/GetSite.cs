using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Workspaces;

namespace Webly.Services.UseCases.Sites;

/// <summary>The site the editor opens on: its head commit, what is published, its domains.</summary>
public class GetSite(
    ISiteRepository siteRepository,
    ISiteVersionRepository versionRepository,
    IDomainRepository domainRepository,
    ISiteWorkspaceRegistry workspaces,
    SiteMapper mapper)
{
    public async Task<Result<SiteError, SiteDetailResponse>> ExecuteAsync(
        int userId,
        string nanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerAsync(nanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError, SiteDetailResponse>.Fail(SiteError.NotFound);

        var head = site.HeadVersion
            ?? throw new InvalidOperationException($"Site '{site.Nanoid}' has no head version.");

        var domains = await domainRepository.ListForSiteAsync(site.Id, cancellationToken);
        var primary = domains.FirstOrDefault(x => x.IsPrimary);

        var published = site.PublishedVersionId is null
            ? null
            : await versionRepository.FindByIdForSiteAsync(site.PublishedVersionId.Value, site.Id, cancellationToken);

        return Result<SiteError, SiteDetailResponse>.Ok(new SiteDetailResponse
        {
            Summary = mapper.ToSummary(site, primary, published?.CreatedAt),
            HeadVersion = SiteMapper.ToVersion(head, site),
            PublishedVersion = published is null ? null : SiteMapper.ToVersion(published, site),
            Domains = [.. domains.Select(SiteMapper.ToDomain)],
            WorkspaceReady = workspaces.Find(site.Nanoid) is not null
        });
    }
}
