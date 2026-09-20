using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Rendering;

/// <summary>
/// Where a site document is provided with the context it cannot know about itself: the name it is
/// published under and the hostname it is canonical at (which lives on a <c>Domain</c> row, not in the
/// document, because the document is the same thing whichever hostname serves it).
/// </summary>
public record RenderContext(string SiteName, string CanonicalBaseUrl)
{
    /// <summary>
    /// Where the page should link its stylesheet, when it must not be the relative path a deployed page uses.
    ///
    /// Exactly one caller sets it: the editor's preview, which serves every page of a site from one URL
    /// (<c>/api/sites/{nanoid}/preview?page=…</c>). A deployed page at <c>/about/index.html</c> correctly links
    /// <c>../styles.css</c>; the same bytes served from the preview URL would resolve that to nothing. An override here
    /// rather than string surgery on the rendered HTML afterwards — patching output is how a renderer stops being the
    /// single description of what a page is.
    /// </summary>
    public string? StylesheetUrl { get; init; }
}

/// <summary>
/// Turns a document into the exact bytes a host should serve. Pure: the same document and context
/// always render the same files, which is what makes a deployment reproducible and a failed one safe to
/// retry — and it is why Webly renders here rather than pushing a repository for the provider to build.
/// A build on somebody else's machine can fail for reasons the site's owner cannot see, let alone fix,
/// and "without touching a single line of code" has to mean there is no build log to read.
/// </summary>
public interface ISiteRenderer
{
    RenderedSite Render(SiteDocument document, RenderContext context);
}
