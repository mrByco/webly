namespace Webly.Data.Models.Sites.Document;

/// <summary>
/// What goes in a page's head. Small on purpose: the fields here are the ones that change what a
/// search result or a shared link looks like, and every one of them is something a person can be
/// asked for in a sentence.
/// </summary>
public class PageSeo
{
    /// <summary>
    /// Overrides <see cref="SitePage.Title"/> in the <c>&lt;title&gt;</c> when set. Two fields because
    /// "Prices" is the right label in the editor's page list and the wrong thing to show in a search
    /// result for a plumber in Utrecht.
    /// </summary>
    public string? MetaTitle { get; set; }

    public string? MetaDescription { get; set; }

    /// <summary>The social preview image, resolved by the renderer like any other image value.</summary>
    public string? OgImage { get; set; }

    /// <summary>
    /// Keeps the page out of search indexes. The renderer writes the meta tag and leaves the page out
    /// of <c>sitemap.xml</c> — two effects of one switch, which is why it is a field and not a note in
    /// the docs telling people to edit a robots file they cannot see.
    /// </summary>
    public bool NoIndex { get; set; }
}
