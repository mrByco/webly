using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Assets;
using Webly.Services.DTO.Common;
using Webly.Services.Services.Repositories;

namespace Webly.Services.UseCases.Assets;

/// <summary>
/// The images in the site as it stands, read from the head commit.
///
/// From the repository rather than from a table, because the repository is where they are: an index of them
/// beside it would be a second list that can disagree with the site — and it would disagree the first time an
/// agent turn deleted one, which is a thing agent turns do.
/// </summary>
public class ListSiteImages(ISiteRepository siteRepository, ISiteRepositoryStore repositories)
{
    public async Task<Result<AssetError, IReadOnlyList<SiteImageResponse>>> ExecuteAsync(
        int userId,
        string siteNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerAsync(siteNanoid, userId, cancellationToken);

        if (site?.HeadVersion is null)
            return Result<AssetError, IReadOnlyList<SiteImageResponse>>.Fail(AssetError.SiteNotFound);

        var entries = await repositories.ListAsync(site.Nanoid, site.HeadVersion.CommitSha, cancellationToken);

        return Result<AssetError, IReadOnlyList<SiteImageResponse>>.Ok(
        [
            .. entries
                .Where(x => x.Path.StartsWith($"{UploadSiteImages.Directory}/", StringComparison.Ordinal))
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .Select(x => new SiteImageResponse
                {
                    Url = $"/{x.Path[("public/".Length)..]}",
                    FileName = x.Path[(UploadSiteImages.Directory.Length + 1)..],
                    Bytes = x.Size
                })
        ]);
    }
}
