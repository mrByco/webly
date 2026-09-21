using Microsoft.AspNetCore.StaticFiles;

namespace Webly.Api.Infrastructure;

/// <summary>
/// The usual extension-to-type mapping, plus the two files Next.js generates without an extension.
///
/// A static export of a site with a generated Open Graph card contains a file literally called
/// <c>opengraph-image</c>, which is a PNG, and the page's own <c>&lt;meta property="og:image"&gt;</c> points at
/// it. <see cref="FileExtensionContentTypeProvider"/> has nothing to go on and answers no, so the static file
/// middleware declines the request — the card 404s while the markup swears it is there, which no amount of
/// reading the HTML reveals.
///
/// Named files rather than <c>ServeUnknownFileTypes</c>: serving everything unrecognised as an image is a
/// fix that turns into a security note the first time somebody's export contains something unexpected.
///
/// The type is not read from the bytes, deliberately. This is a development convenience in front of output
/// <i>Webly's own build</i> produced, and the two names are Next's own conventions — sniffing would be a
/// general-purpose mechanism where a two-entry list is the whole truth.
/// </summary>
public class NextMetadataImageContentTypeProvider : IContentTypeProvider
{
    private static readonly FileExtensionContentTypeProvider ByExtension = new();

    /// <summary>Next's metadata image routes, as a static export names them on disk.</summary>
    private static readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["opengraph-image"] = "image/png",
        ["twitter-image"] = "image/png"
    };

    public bool TryGetContentType(string subpath, out string contentType)
    {
        if (ByExtension.TryGetContentType(subpath, out contentType!)) return true;

        return ByName.TryGetValue(Path.GetFileName(subpath), out contentType!);
    }
}
