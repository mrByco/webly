using Webly.Data;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Services.Services.Sites;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Appends a draft to a site's history and points the site at it. <b>The only way a site ever changes.</b>
///
/// Every edit path in Webly ends here — an agent turn, the property editor, a restore — so there is one place
/// that decides what a version is: the draft's document, a parent pointer to what it came from, a summary, and
/// who caused it. A second write path would be a second definition of history.
///
/// Does nothing for a draft with no changes, and returns null. A turn that only answered a question must not
/// leave a version behind that says "no changes" — a history is only useful if every entry is a change.
/// </summary>
public class CommitSiteVersion(
    ISiteVersionRepository versionRepository,
    WeblyDbContext dbContext)
{
    public async Task<SiteVersion?> ExecuteAsync(
        Site site,
        SiteDraft draft,
        int userId,
        SiteVersionOrigin origin,
        int? sourceMessageId = null,
        int? restoredFromVersionId = null,
        string? summary = null,
        CancellationToken cancellationToken = default)
    {
        if (!draft.IsDirty) return null;

        var version = new SiteVersion
        {
            SiteId = site.Id,
            ParentVersionId = site.DraftVersionId,
            Document = draft.Document,
            Summary = summary ?? draft.Summarize(),
            Origin = origin,
            CreatedByUserId = userId,
            SourceMessageId = sourceMessageId,
            RestoredFromVersionId = restoredFromVersionId
        };

        versionRepository.Add(version);

        // Saved before the pointer moves, because the pointer needs the new row's id — and because a version
        // that exists without being pointed at is a harmless orphan, while a pointer to a row that was never
        // written is a site that cannot be opened.
        await dbContext.SaveChangesAsync(cancellationToken);

        site.DraftVersionId = version.Id;
        await dbContext.SaveChangesAsync(cancellationToken);

        return version;
    }
}
