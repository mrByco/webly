using Microsoft.Extensions.Logging;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Repositories;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// The site's whole repository, as a file the person can take away.
///
/// This is the feature that makes "a site is real source code, and it is yours" checkable instead of merely
/// stated. The product's central bet is that generated source beats a proprietary document, and the strongest
/// form of that bet is being able to leave: `git clone site.bundle` gives a working Next.js project with every
/// version, every commit message and every sentence the agent wrote about what it changed.
///
/// A bundle rather than a zip, for that reason. A zip of the current files is a snapshot; a bundle is the
/// history. And a bundle rather than a push to the customer's GitHub, for now, because that needs their
/// credentials and an OAuth flow, while this needs a button — see <c>MASTER_PLAN.md</c> P6, where the push is
/// the second half.
///
/// <b>No way back in.</b> There is deliberately no import: a commit Webly never validated could break the
/// build, the renderer's assumptions, or the agent's next turn, and the way to change a site is to ask.
/// </summary>
public class ExportSite(
    ISiteRepository siteRepository,
    ISiteRepositoryStore repositories,
    ILogger<ExportSite> logger)
{
    public async Task<Result<SiteError, SiteExport>> ExecuteAsync(
        int userId,
        string siteNanoid,
        CancellationToken cancellationToken = default)
    {
        // The light variant: this needs the row's slug and branch, not its versions.
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError, SiteExport>.Fail(SiteError.NotFound);

        try
        {
            var bundle = await repositories.CreateBundleAsync(siteNanoid, site.DefaultBranch, cancellationToken);

            // The slug, not the name: a name can be "Bob's Bikes & Co." and this ends up as a filename on
            // somebody's desktop. The slug is already URL-safe by construction, which is the same constraint.
            return Result<SiteError, SiteExport>.Ok(new SiteExport($"{site.Slug}.bundle", bundle));
        }
        catch (RepositoryException exception)
        {
            // Logged here rather than returned to the caller: git's stderr names server-side paths, and the
            // controller deliberately does not echo it. This is the only place it is kept.
            logger.LogError(exception, "Bundling {Site} failed: {Detail}", siteNanoid, exception.Detail);

            return Result<SiteError, SiteExport>.Fail(SiteError.RepositoryFailed);
        }
    }
}

/// <summary>A downloadable copy of a site's repository.</summary>
/// <param name="FileName">What it should be called when it lands on somebody's machine.</param>
/// <param name="Content">The bundle itself.</param>
public record SiteExport(string FileName, byte[] Content);
