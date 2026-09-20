using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;

namespace Webly.Services.UseCases.Sites;

/// <summary>The history, newest first, without documents. Paged, because a chatty week is a hundred versions.</summary>
public class ListSiteVersions(ISiteRepository siteRepository, ISiteVersionRepository versionRepository)
{
    public async Task<Result<SiteError, IReadOnlyList<SiteVersionResponse>>> ExecuteAsync(
        int userId,
        string nanoid,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(nanoid, userId, cancellationToken);

        if (site is null)
            return Result<SiteError, IReadOnlyList<SiteVersionResponse>>.Fail(SiteError.NotFound);

        var versions = await versionRepository.ListForSiteAsync(site.Id, skip, Math.Clamp(take, 1, 100), cancellationToken);

        return Result<SiteError, IReadOnlyList<SiteVersionResponse>>.Ok(
            [.. versions.Select(x => SiteMapper.ToVersion(x, site, includeDocument: false))]);
    }
}
