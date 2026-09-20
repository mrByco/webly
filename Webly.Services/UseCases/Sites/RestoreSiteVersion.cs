using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Sites;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Goes back to an earlier version — by <b>copying it forward</b>, never by moving the draft pointer backwards.
///
/// That is the decision that makes the history trustworthy. A restore is itself an edit, so it appears in the
/// history as one ("Restored the version from Tuesday"), the versions it stepped over are still there, and undoing
/// an undo is the same operation again. Repointing at an old version would instead make the intervening history
/// unreachable — the one thing a version control feature must never do.
///
/// The restored document is re-validated on the way in. A document written under an older catalogue can be invalid
/// today — a section type removed, a field's bounds tightened — and a restore is exactly when that surfaces. Better
/// a refusal that names the problem than a published page that no longer renders.
/// </summary>
public class RestoreSiteVersion(
    ISiteRepository siteRepository,
    ISiteVersionRepository versionRepository,
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

        if (source is null) return Result<SiteError, SiteVersionResponse>.Fail(SiteError.VersionNotFound);

        var summary = $"Restored the version from {source.CreatedAt:d MMMM, HH:mm} — {source.Summary}";

        // Built through a draft like every other edit, so the commit path is the same one — the draft starts from
        // the current document and Replace puts the restored one in, which is what marks it dirty.
        var draft = SiteDraft.From(site.DraftVersion!.Document);
        var replaced = draft.Replace(source.Document, summary);

        if (!replaced.Succeeded)
            return Result<SiteError, SiteVersionResponse>.Fail(SiteError.InvalidDocument, replaced.Message);

        var version = await commitSiteVersion.ExecuteAsync(
            site, draft, userId, SiteVersionOrigin.Restore,
            restoredFromVersionId: source.Id,
            summary: summary,
            cancellationToken: cancellationToken);

        return version is null
            ? Result<SiteError, SiteVersionResponse>.Fail(SiteError.InvalidDocument)
            : Result<SiteError, SiteVersionResponse>.Ok(SiteMapper.ToVersion(version, site, includeDocument: true));
    }
}
