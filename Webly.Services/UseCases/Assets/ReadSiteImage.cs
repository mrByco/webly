using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Assets;
using Webly.Services.DTO.Common;
using Webly.Services.Services.Repositories;

namespace Webly.Services.UseCases.Assets;

/// <summary>
/// One of the site's images, for the editor to show.
///
/// <b>The site's own pages must never point here</b>, and the controller says so at length: a published page
/// serves its pictures from its own domain, out of the export, and a page that fetched them through Webly
/// would stop working the moment somebody looked at it without being signed in. This is the editor's own
/// route, for the one thing the site's URL cannot do — show a photograph in a site that has never been
/// published, or whose sandbox is asleep, to the person deciding whether to delete it.
///
/// From the head commit rather than the workspace, like the code view: what a person is looking at is the
/// version their site is, not whatever a sandbox holds this second.
/// </summary>
public class ReadSiteImage(
    ISiteRepository siteRepository,
    ISiteVersionRepository versionRepository,
    ISiteRepositoryStore repositories)
{
    public async Task<Result<AssetError, SiteImageContent>> ExecuteAsync(
        int userId,
        string siteNanoid,
        string fileName,
        string? versionNanoid = null,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerAsync(siteNanoid, userId, cancellationToken);

        if (site?.HeadVersion is null) return Fail(AssetError.SiteNotFound);

        // A name, never a path. `..`, a slash or a backslash here would be a way to read any file in the site
        // through an endpoint that answers with bytes and a content type — the repository store refuses those
        // too, and this is the layer that knows the argument is supposed to be one segment.
        if (fileName.Length == 0
            || fileName.Contains('/', StringComparison.Ordinal)
            || fileName.Contains('\\', StringComparison.Ordinal)
            || fileName.Contains("..", StringComparison.Ordinal))
            return Fail(AssetError.NotFound);

        // Which version's bytes. The head is what the settings screen's thumbnails want — "these are your
        // photographs" is a question about now — and a named version is what the history's diff wants, because
        // the picture a version *added* is not necessarily the one at that name today, and may not be there at
        // all. Reading head for both would have shown the wrong photograph after a replacement and a broken
        // image after a delete, on the screen whose job is to say what a version did.
        //
        // Resolved through the version repository rather than trusted, so an id belonging to another site
        // answers not-found exactly as an invented one does.
        var commitSha = site.HeadVersion.CommitSha;

        if (versionNanoid is not null)
        {
            var version = await versionRepository.FindForSiteAsync(versionNanoid, site.Id, cancellationToken);

            if (version is null) return Fail(AssetError.NotFound);

            commitSha = version.CommitSha;
        }

        var file = await repositories.ReadFileAsync(
            site.Nanoid, commitSha, $"{UploadSiteImages.Directory}/{fileName}", cancellationToken);

        if (file is null) return Fail(AssetError.NotFound);

        var kind = ImageKind.Of(file.Content);

        // Not an image any more — an agent turn can write anything into that directory. Refused rather than
        // served as bytes of an unknown type, because "whatever this is, here it is" is how an origin serves
        // somebody else's HTML.
        if (kind is null) return Fail(AssetError.NotAnImage, fileName);

        return Result<AssetError, SiteImageContent>.Ok(new SiteImageContent(file.Content, kind.ContentType));
    }

    private static Result<AssetError, SiteImageContent> Fail(AssetError error, string? detail = null) =>
        Result<AssetError, SiteImageContent>.Fail(error, detail);
}
