namespace Webly.Data.Models.Sites.Document;

/// <summary>
/// The header and footer, which are site-wide rather than per page — a person who asks for "a link
/// to the new page in the menu" means every page, and a navigation stored per page is how one page
/// ends up missing the link.
/// </summary>
public class SiteNavigation
{
    /// <summary>Header links, in order.</summary>
    public List<NavigationLink> Header { get; set; } = [];

    /// <summary>Footer links, in order. Usually the legal ones the header omits.</summary>
    public List<NavigationLink> Footer { get; set; } = [];

    /// <summary>
    /// The header's single call to action, rendered as a button rather than a link. Nullable because a
    /// five-page brochure site has nothing to sell from its header.
    /// </summary>
    public NavigationLink? PrimaryAction { get; set; }

    /// <summary>The line in the footer. <c>{year}</c> is substituted at render time.</summary>
    public string? FooterNote { get; set; }
}

/// <summary>
/// A link to one of the site's own pages, or to somewhere else entirely. Exactly one of
/// <see cref="PageId"/> and <see cref="Url"/> is set — the validator enforces it, the same "exactly
/// one subject" shape Cookta's <c>Unit</c> and stock rows use, for the same reason: a link that names
/// both has no defined meaning, and a renderer would have to guess.
///
/// Naming a page by id rather than by path is what makes renaming a page's URL safe: the menu follows
/// it instead of quietly 404ing.
/// </summary>
public class NavigationLink
{
    public required string Label { get; set; }

    public string? PageId { get; set; }

    public string? Url { get; set; }

    /// <summary>Opens in a new tab. Only meaningful for an external <see cref="Url"/>.</summary>
    public bool NewTab { get; set; }
}
