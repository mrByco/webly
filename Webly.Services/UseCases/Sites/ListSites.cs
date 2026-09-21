using Webly.Data.Repositories.Deployments;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Sites;

namespace Webly.Services.UseCases.Sites;

public class ListSites(
    ISiteRepository siteRepository,
    IDomainRepository domainRepository,
    ISiteVersionRepository versionRepository,
    IDeploymentRepository deploymentRepository,
    SiteMapper mapper)
{
    public async Task<IReadOnlyList<SiteSummaryResponse>> ExecuteAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var sites = await siteRepository.ListForOwnerAsync(userId, cancellationToken);
        var summaries = new List<SiteSummaryResponse>(sites.Count);

        foreach (var site in sites)
        {
            var primary = await domainRepository.FindPrimaryAsync(site.Id, cancellationToken);

            // The published version's timestamp, not the site's: "live since" is a fact about the deployed
            // version, and the site row moves every time somebody types in the editor.
            var publishedAt = site.PublishedVersionId is null
                ? null
                : (await versionRepository.FindByIdForSiteAsync(site.PublishedVersionId.Value, site.Id, cancellationToken))?.CreatedAt;

            var live = site.PublishedVersionId is null
                ? null
                : await deploymentRepository.FindLiveAsync(site.Id, site.PublishedVersionId.Value, cancellationToken);

            summaries.Add(mapper.ToSummary(site, primary, publishedAt, live));
        }

        return summaries;
    }
}
