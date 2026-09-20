using Microsoft.Extensions.Logging;
using Webly.Data;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Services.Services.Repositories;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Commits a working tree and records it as a version. <b>The only way a site ever changes.</b>
///
/// Every path ends here — an agent turn, a hand edit, a restore — so there is one place that decides what a
/// version is: a commit on the branch, a row naming it, and the site's head moved to it. A second write path
/// would be a second definition of history, and they would disagree the first time one of them was changed.
///
/// Returns null when the tree is identical to the head's. A turn that answered a question without touching a
/// file must leave no commit and no row: a history of empty entries is worse than a short one.
/// </summary>
public class CommitSiteVersion(
    ISiteRepositoryStore repositories,
    ISiteVersionRepository versionRepository,
    WeblyDbContext dbContext,
    ILogger<CommitSiteVersion> logger)
{
    public async Task<SiteVersion?> ExecuteAsync(
        Site site,
        WorkspaceTree tree,
        CommitAuthor author,
        int userId,
        SiteVersionOrigin origin,
        string summary,
        string? details = null,
        int? sourceMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var head = site.HeadVersion
            ?? throw new InvalidOperationException($"Site '{site.Nanoid}' has no head version.");

        var commit = await repositories.CommitAsync(
            site.Nanoid, site.DefaultBranch, head.CommitSha, tree, author, summary, details, cancellationToken);

        if (commit is null) return null;

        return await RecordAsync(site, head, commit, userId, origin, summary, details, sourceMessageId,
            restoredFromVersionId: null, cancellationToken);
    }

    /// <summary>
    /// Records a commit the store has already made — the restore path, which produces its commit from an old
    /// tree rather than from a workspace. Same row, same pointer move, so the history cannot tell them apart in
    /// any way that matters.
    /// </summary>
    public async Task<SiteVersion> RecordAsync(
        Site site,
        SiteVersion parent,
        CommitResult commit,
        int userId,
        SiteVersionOrigin origin,
        string summary,
        string? details,
        int? sourceMessageId,
        int? restoredFromVersionId,
        CancellationToken cancellationToken = default)
    {
        var version = new SiteVersion
        {
            SiteId = site.Id,
            CommitSha = commit.Sha,
            ParentVersionId = parent.Id,
            Summary = summary,
            Details = details,
            Origin = origin,
            ChangedFileCount = commit.ChangedFileCount,
            CreatedByUserId = userId,
            SourceMessageId = sourceMessageId,
            RestoredFromVersionId = restoredFromVersionId
        };

        versionRepository.Add(version);

        // Saved before the pointer moves, because the pointer needs the new row's id — and because a version
        // that exists without being pointed at is a harmless orphan, while a pointer to a row that was never
        // written is a site that cannot be opened.
        await dbContext.SaveChangesAsync(cancellationToken);

        site.HeadVersionId = version.Id;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Site {Site} is now at {Commit} ({Files} files): {Summary}",
            site.Nanoid, commit.Sha[..7], commit.ChangedFileCount, summary);

        return version;
    }
}
