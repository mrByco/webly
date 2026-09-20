using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;

namespace Webly.Services.UseCases.Sites;

/// <summary>One version with its document, for previewing a point in the site's past.</summary>
public class GetSiteVersion(ISiteRepository siteRepository, ISiteVersionRepository versionRepository)
{
    public async Task<Result<SiteError, SiteVersionResponse>> ExecuteAsync(
        int userId,
        string siteNanoid,
        string versionNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError, SiteVersionResponse>.Fail(SiteError.NotFound);

        var version = await versionRepository.FindForSiteAsync(versionNanoid, site.Id, cancellationToken);

        if (version is null) return Result<SiteError, SiteVersionResponse>.Fail(SiteError.VersionNotFound);

        return Result<SiteError, SiteVersionResponse>.Ok(SiteMapper.ToVersion(version, site, includeDocument: true));
    }
}
