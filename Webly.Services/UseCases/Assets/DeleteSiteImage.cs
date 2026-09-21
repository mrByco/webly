using Microsoft.Extensions.Logging;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Common;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Workspaces;
using Webly.Services.UseCases.Sites;

namespace Webly.Services.UseCases.Assets;

/// <summary>
/// Removes a photograph from the site, as a version of its own.
///
/// <b>Refused while a page still uses it.</b> A delete that leaves an <c>&lt;img&gt;</c> pointing at nothing
/// is a broken page on a real business's website, produced by a button that said nothing about it — and the
/// person deleting is the one who least expects that, because to them it is a photograph they no longer want
/// rather than a file something references. So the tree is searched for the URL first and the answer names
/// the files, which is what makes "ask the assistant to take it off the home page" a thing somebody can
/// actually do.
///
/// The image stays in the history, as everything does: this writes a version in which it is absent, and the
/// version before it still has it. A site's history is what makes a mistake undoable, and that has to include
/// this one.
/// </summary>
public class DeleteSiteImage(
    ISiteRepository siteRepository,
    IUserRepository users,
    ISiteRepositoryStore repositories,
    ISiteWorkspaceRegistry workspaces,
    CommitSiteVersion commitSiteVersion,
    ILogger<DeleteSiteImage> logger)
{
    public async Task<Result<AssetError>> ExecuteAsync(
        int userId,
        string siteNanoid,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerAsync(siteNanoid, userId, cancellationToken);

        if (site?.HeadVersion is null) return Result<AssetError>.Fail(AssetError.SiteNotFound);

        var user = await users.FindByIdAsync(userId, cancellationToken);

        if (user is null) return Result<AssetError>.Fail(AssetError.SiteNotFound);

        var path = $"{UploadSiteImages.Directory}/{fileName}";
        var tree = await repositories.ReadTreeAsync(site.Nanoid, site.HeadVersion.CommitSha, cancellationToken);

        if (tree.Find(path) is null) return Result<AssetError>.Fail(AssetError.NotFound);

        // How the site refers to it: everything under `public/` is served from the root, so a page says
        // `/images/shopfront.jpg`. Searched as text, because that is how it appears — in a `src`, in a CSS
        // `url()`, in a metadata block — and a parser for each of those would be three ways to miss one.
        // Which files count as a page, and why prose does not, is `ImageReferences`.
        var url = $"/{path["public/".Length..]}";

        var used = ImageReferences.UsedBy(tree, url);

        if (used.Count > 0)
            return Result<AssetError>.Fail(AssetError.InUse, string.Join(", ", used));

        var version = await commitSiteVersion.ExecuteAsync(
            site,
            // Deletion by absence, which is how every other change to a site is expressed.
            new WorkspaceTree([.. tree.Files.Where(x => x.Path != path)]),
            new CommitAuthor(user.DisplayName, user.Email),
            userId,
            SiteVersionOrigin.Upload,
            $"Removed {fileName}",
            details: $"{url} is no longer in the site. Earlier versions still have it.",
            cancellationToken: cancellationToken);

        if (version is not null)
        {
            await workspaces.ReseedAsync(site, version.CommitSha, cancellationToken);

            logger.LogInformation("{File} removed from {Site} as {Version}.", fileName, site.Nanoid, version.Nanoid);
        }

        return Result<AssetError>.Ok();
    }
}
