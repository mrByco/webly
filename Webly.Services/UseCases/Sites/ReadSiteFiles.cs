using System.Text;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Repositories;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// The site's own source, for the editor's code view.
///
/// It exists because the product's promise is "you never have to touch the code", not "you are not allowed to
/// see it". Somebody who wants to look — or to hand the project to a developer later — should find real files
/// rather than a proprietary document, and this is what makes that visible from inside the app.
///
/// Reads from a commit, never from the live workspace: the workspace is mid-edit by definition, and a person
/// reading their own site's source should see the version that was committed rather than whatever a sandbox
/// happens to hold this second.
/// </summary>
public class ReadSiteFiles(
    ISiteRepository siteRepository,
    ISiteVersionRepository versionRepository,
    ISiteRepositoryStore repositories)
{
    /// <summary>Anything larger is not something to put in a text editor in a browser.</summary>
    private const long MaxTextBytes = 512 * 1024;

    public async Task<Result<SiteError, IReadOnlyList<SiteFileEntryResponse>>> ListAsync(
        int userId,
        string siteNanoid,
        string? versionNanoid = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(userId, siteNanoid, versionNanoid, cancellationToken);

        if (resolved.Error is { } error)
            return Result<SiteError, IReadOnlyList<SiteFileEntryResponse>>.Fail(error);

        var entries = await repositories.ListAsync(siteNanoid, resolved.CommitSha!, cancellationToken);

        return Result<SiteError, IReadOnlyList<SiteFileEntryResponse>>.Ok(
            [.. entries.OrderBy(x => x.Path, StringComparer.Ordinal)
                .Select(x => new SiteFileEntryResponse { Path = x.Path, Size = x.Size })]);
    }

    public async Task<Result<SiteError, SiteFileResponse>> ReadAsync(
        int userId,
        string siteNanoid,
        string path,
        string? versionNanoid = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(userId, siteNanoid, versionNanoid, cancellationToken);

        if (resolved.Error is { } error) return Result<SiteError, SiteFileResponse>.Fail(error);

        var file = await repositories.ReadFileAsync(siteNanoid, resolved.CommitSha!, path, cancellationToken);

        if (file is null) return Result<SiteError, SiteFileResponse>.Fail(SiteError.FileNotFound);

        return Result<SiteError, SiteFileResponse>.Ok(new SiteFileResponse
        {
            Path = file.Path,
            Size = file.Content.Length,
            // Binary is answered as "here is its size", not as mojibake: a browser asked to render a PNG as
            // UTF-8 shows a screenful of nonsense and no clue why.
            Text = file.Content.Length <= MaxTextBytes && IsProbablyText(file.Content)
                ? Encoding.UTF8.GetString(file.Content)
                : null
        });
    }

    /// <summary>The unified diff of one version, for the history's detail view.</summary>
    public async Task<Result<SiteError, string>> DiffAsync(
        int userId,
        string siteNanoid,
        string versionNanoid,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(userId, siteNanoid, versionNanoid, cancellationToken);

        if (resolved.Error is { } error) return Result<SiteError, string>.Fail(error);

        return Result<SiteError, string>.Ok(
            await repositories.DiffAsync(siteNanoid, resolved.CommitSha!, cancellationToken));
    }

    /// <summary>
    /// Ownership, then the commit — a named version's, or the head's. One method, because every read here needs
    /// exactly this and doing it three times is three chances to forget the first half.
    /// </summary>
    private async Task<(SiteError? Error, string? CommitSha)> ResolveAsync(
        int userId,
        string siteNanoid,
        string? versionNanoid,
        CancellationToken cancellationToken)
    {
        var site = await siteRepository.FindForOwnerAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return (SiteError.NotFound, null);

        if (versionNanoid is null or "")
            return site.HeadVersion is { } head ? (null, head.CommitSha) : (SiteError.VersionNotFound, null);

        var version = await versionRepository.FindForSiteAsync(versionNanoid, site.Id, cancellationToken);

        return version is null ? (SiteError.VersionNotFound, null) : (null, version.CommitSha);
    }

    /// <summary>
    /// A NUL byte in the first kilobyte. Crude, and the same heuristic git itself uses — which is the right
    /// reference point, because these files came out of git.
    /// </summary>
    private static bool IsProbablyText(byte[] content) =>
        !content.Take(1024).Contains((byte)0);
}
