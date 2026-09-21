using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Workspaces;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Goes back to an earlier version — by <b>writing its tree forward as a new commit</b>, never by moving the
/// branch backwards.
///
/// That is the decision that makes the history trustworthy. A restore is itself a change: it appears in the
/// history ("Restored the version from Tuesday"), the versions it stepped over are still there, and undoing an
/// undo is the same operation again. Resetting the branch would instead make the intervening history
/// unreachable — the one thing a version-control feature must never do — and would leave any deployment that
/// named one of those commits pointing at nothing.
/// </summary>
public class RestoreSiteVersion(
    ISiteRepository siteRepository,
    ISiteVersionRepository versionRepository,
    IUserRepository users,
    ISiteRepositoryStore repositories,
    ISiteWorkspaceRegistry workspaces,
    CommitSiteVersion commitSiteVersion)
{
    public async Task<Result<SiteError, SiteVersionResponse>> ExecuteAsync(
        int userId,
        string siteNanoid,
        string versionNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError, SiteVersionResponse>.Fail(SiteError.NotFound);

        var source = await versionRepository.FindForSiteAsync(versionNanoid, site.Id, cancellationToken);
        var head = site.HeadVersion;

        if (source is null || head is null)
            return Result<SiteError, SiteVersionResponse>.Fail(SiteError.VersionNotFound);

        var user = await users.FindByIdAsync(userId, cancellationToken);

        if (user is null) return Result<SiteError, SiteVersionResponse>.Fail(SiteError.NotFound);

        var summary = $"Restored the version from {source.CreatedAt:d MMMM, HH:mm}";

        var commit = await repositories.RestoreAsync(
            site.Nanoid, site.DefaultBranch, head.CommitSha, source.CommitSha,
            new CommitAuthor(user.DisplayName, user.Email), summary, cancellationToken);

        // Identical tree: the version asked for is already what the site says. Not an error — somebody clicked
        // "bring this back" on the thing that is already live, and the honest answer is the current version.
        if (commit is null)
            return Result<SiteError, SiteVersionResponse>.Ok(SiteMapper.ToVersion(head, site));

        var version = await commitSiteVersion.RecordAsync(
            site, head, commit, userId, SiteVersionOrigin.Restore, summary,
            details: $"Restored from {source.CommitSha[..7]} — {source.Summary}",
            sourceMessageId: null,
            restoredFromVersionId: source.Id,
            cancellationToken);

        // The live workspace is now a commit behind, and its sandbox holds the tree this restore just replaced. It
        // used to be released here, and that was wrong in the most visible way available: the preview the person
        // was looking at when they pressed "bring this back" became "your preview is not running, send a message to
        // wake it up", and a warm sandbox worth twelve seconds went with it. Re-seeded instead — the same work the
        // next lease would have done, including clearing the agent's session — so the dev server recompiles the
        // restored files and the preview shows them by itself.
        await workspaces.ReseedAsync(site, version.CommitSha, cancellationToken);

        return Result<SiteError, SiteVersionResponse>.Ok(SiteMapper.ToVersion(version, site));
    }
}
