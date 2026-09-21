using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Assets;
using Webly.Services.DTO.Common;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Workspaces;
using Webly.Services.UseCases.Sites;

namespace Webly.Services.UseCases.Assets;

/// <summary>
/// Puts a customer's own photographs into their site.
///
/// <b>An image is a version, not an asset.</b> There is no blob store here and no asset table: the files are
/// committed into the site's repository under <c>public/</c>, which is where Next.js serves static files
/// from. Four things fall out of that and each of them is the reason for it:
///
/// <list type="bullet">
/// <item>The published site serves its own photographs from its own domain. Nothing at build time has to fetch
/// them, and nothing of Webly's has to still exist for them to load.</item>
/// <item>They travel with the export. <c>git clone site.bundle</c> gives somebody their pictures as well as
/// their code, which is what makes "it is your site" true rather than nearly true.</item>
/// <item>They are in the history, so a restore takes them back and a version that references one cannot find
/// it missing.</item>
/// <item><see cref="CommitSiteVersion"/> stays the only writer of history. An upload that wrote files by
/// another path would be a second definition of what a version is.</item>
/// </list>
///
/// The cost is that a site's repository now carries binary, and that is what the size caps here are about. A
/// business website wants a dozen photographs, not a library; the browser downscales before it uploads
/// (`image-file.ts` in the client), and this is the floor under that rather than a substitute for it.
/// </summary>
public class UploadSiteImages(
    ISiteRepository siteRepository,
    IUserRepository users,
    ISiteRepositoryStore repositories,
    ISiteWorkspaceRegistry workspaces,
    CommitSiteVersion commitSiteVersion,
    IOptions<RepositoryOptions> repositoryOptions,
    ILogger<UploadSiteImages> logger)
{
    /// <summary>Where images live in the repository. Under <c>public/</c>, so the site serves them at <c>/images/…</c>.</summary>
    public const string Directory = "public/images";

    /// <summary>The most one image may weigh once it reaches us.</summary>
    public const int MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>The most one upload may carry, so a drag of forty photographs is refused before it is read.</summary>
    public const int MaxImagesPerUpload = 10;

    public async Task<Result<AssetError, UploadImagesResponse>> ExecuteAsync(
        int userId,
        string siteNanoid,
        IReadOnlyList<UploadedImage> uploads,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Fail(AssetError.SiteNotFound);
        if (uploads.Count == 0) return Fail(AssetError.Empty);
        if (uploads.Count > MaxImagesPerUpload)
            return Fail(AssetError.TooLarge, $"{uploads.Count} files at once is more than {MaxImagesPerUpload}.");

        var head = site.HeadVersion;
        var user = await users.FindByIdAsync(userId, cancellationToken);

        if (head is null || user is null) return Fail(AssetError.SiteNotFound);

        var tree = await repositories.ReadTreeAsync(site.Nanoid, head.CommitSha, cancellationToken);
        var files = tree.Files.ToList();
        var taken = new HashSet<string>(files.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
        var added = new List<SiteImageResponse>();

        foreach (var upload in uploads)
        {
            if (upload.Content.Length > MaxImageBytes)
                return Fail(AssetError.TooLarge, $"{upload.FileName} is larger than {MaxImageBytes / (1024 * 1024)} MB.");

            var kind = ImageKind.Of(upload.Content);

            if (kind is null)
                return Fail(AssetError.NotAnImage, $"{upload.FileName} is not a JPEG, PNG, GIF or WebP.");

            var path = $"{Directory}/{UniqueName(upload.FileName, kind.Extension, taken)}";

            taken.Add(path);
            files.Add(new WorkspaceFile(path, upload.Content));

            added.Add(new SiteImageResponse
            {
                // The site's own URL for it, which is the repository path without `public/`.
                Url = $"/{path[("public/".Length)..]}",
                FileName = path[(Directory.Length + 1)..],
                Bytes = upload.Content.Length
            });
        }

        var total = files.Sum(x => (long)x.Content.Length);

        // Checked here as well as in the repository store, because the store's refusal is a sentence about a
        // tree and this one can name the thing the person just did.
        if (total > repositoryOptions.Value.MaxTreeBytes)
            return Fail(
                AssetError.TooLarge,
                "Your site would be larger than it is allowed to be. Remove some images before adding more.");

        var summary = added.Count == 1
            ? $"Added {added[0].FileName}"
            : $"Added {added.Count} images";

        var version = await commitSiteVersion.ExecuteAsync(
            site,
            new WorkspaceTree(files),
            new CommitAuthor(user.DisplayName, user.Email),
            userId,
            SiteVersionOrigin.Upload,
            summary,
            details: string.Join('\n', added.Select(x => $"{x.Url} ({x.Bytes / 1024} KB)")),
            cancellationToken: cancellationToken);

        // Null when every uploaded file was already in the site, byte for byte. Not an error: the person gets
        // the paths back and can use them, and a history entry for "added the photo that was already there"
        // would be a version that changed nothing.
        if (version is not null)
        {
            // The same reason a restore re-seeds rather than releasing: somebody is looking at the preview,
            // and the next thing they will do is ask for the picture to be used. A workspace still holding the
            // tree from before the upload would make the agent's first act be to wonder where the file is.
            await workspaces.ReseedAsync(site, version.CommitSha, cancellationToken);

            logger.LogInformation(
                "{Count} image(s) committed to {Site} as {Version}.", added.Count, site.Nanoid, version.Nanoid);
        }

        return Result<AssetError, UploadImagesResponse>.Ok(new UploadImagesResponse
        {
            Images = added,
            VersionNanoid = version?.Nanoid
        });
    }

    /// <summary>
    /// A file name a site can live with: lowercase, no spaces, the extension the bytes earned, and never one
    /// that is already taken.
    ///
    /// The original name is kept as far as it can be, because it is the only handle the person has on which
    /// photograph is which — <c>shopfront.jpg</c> in a message means something and <c>a7f3c21.jpg</c> does
    /// not. A collision gets a number rather than overwriting: two photographs called <c>image.jpg</c> from
    /// two phones are a normal afternoon, and silently replacing the first would remove it from a page that
    /// is already using it.
    /// </summary>
    private static string UniqueName(string original, string extension, HashSet<string> taken)
    {
        var stem = Path.GetFileNameWithoutExtension(original);
        var cleaned = new string([.. stem.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')])
            .Trim('-');

        while (cleaned.Contains("--", StringComparison.Ordinal))
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);

        if (cleaned.Length == 0) cleaned = "image";
        if (cleaned.Length > 40) cleaned = cleaned[..40].Trim('-');

        var candidate = $"{Directory}/{cleaned}.{extension}";

        for (var suffix = 2; taken.Contains(candidate); suffix++)
            candidate = $"{Directory}/{cleaned}-{suffix}.{extension}";

        return candidate[(Directory.Length + 1)..];
    }

    private static Result<AssetError, UploadImagesResponse> Fail(AssetError error, string? detail = null) =>
        Result<AssetError, UploadImagesResponse>.Fail(error, detail);
}
