using Webly.Data.Repositories.Deployments;
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
    IDeploymentRepository deploymentRepository,
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

        // The deployment serving that version, which is where the header's link goes until the site's own address
        // is arranged with the provider. One query on a screen that already makes several, and none for a site
        // nobody has published.
        var live = site.PublishedVersionId is null
            ? null
            : await deploymentRepository.FindLiveAsync(site.Id, site.PublishedVersionId.Value, cancellationToken);

        return Result<SiteError, SiteDetailResponse>.Ok(new SiteDetailResponse
        {
            Summary = mapper.ToSummary(site, primary, published?.CreatedAt, live),
            HeadVersion = SiteMapper.ToVersion(head, site),
            PublishedVersion = published is null ? null : SiteMapper.ToVersion(published, site),
            Domains = [.. domains.Select(SiteMapper.ToDomain)],
            WorkspaceReady = await workspaces.IsPreviewReadyAsync(site.Nanoid, cancellationToken)
        });
    }
}
