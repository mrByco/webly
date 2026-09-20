using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Sites;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// A hand edit from the property editor: one section's fields, patched.
///
/// Through the same <see cref="SiteDraft"/> and the same <see cref="CommitSiteVersion"/> as an agent turn, which is
/// the point — the person and the agent edit the same way, so a hand edit is validated by the same rules, appears
/// in the same history, and can be restored the same way. The only difference is the version's <c>Origin</c>.
///
/// One version per hand edit is deliberate, and it is why the client debounces: a version per keystroke would be a
/// history nobody can read. The editor commits when a field loses focus.
/// </summary>
public class EditSection(
    ISiteRepository siteRepository,
    CommitSiteVersion commitSiteVersion)
{
    public async Task<Result<SiteError, SiteVersionResponse>> ExecuteAsync(
        int userId,
        string siteNanoid,
        EditSectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError, SiteVersionResponse>.Fail(SiteError.NotFound);

        var document = site.DraftVersion?.Document
            ?? throw new InvalidOperationException($"Site '{site.Nanoid}' has no draft version.");

        var draft = SiteDraft.From(document);
        var edit = draft.UpdateSection(request.SectionId, request.Props);

        if (!edit.Succeeded)
            return Result<SiteError, SiteVersionResponse>.Fail(SiteError.InvalidDocument, edit.Message);

        var version = await commitSiteVersion.ExecuteAsync(
            site, draft, userId, SiteVersionOrigin.Manual, cancellationToken: cancellationToken);

        return version is null
            ? Result<SiteError, SiteVersionResponse>.Fail(SiteError.InvalidDocument)
            : Result<SiteError, SiteVersionResponse>.Ok(SiteMapper.ToVersion(version, site, includeDocument: true));
    }
}
