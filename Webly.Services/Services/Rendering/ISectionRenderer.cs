using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Rendering;

/// <summary>
/// The markup for one section type — the one piece of per-type code the catalogue cannot generate,
/// because it is the design. Registered by <see cref="SectionType"/>, so a missing implementation is
/// caught by <c>SectionCatalogueTests.Every_section_type_has_a_schema_and_a_renderer</c> rather than by a
/// blank band on a published page.
/// </summary>
public interface ISectionRenderer
{
    SectionType Type { get; }

    /// <summary>
    /// The section as an HTML fragment. Implementations read props through
    /// <see cref="SectionMarkup"/>'s helpers, which escape everything by default — the values here were
    /// written by a language model and typed by a member of the public, and neither is a reason to
    /// trust a string into markup.
    /// </summary>
    string Render(SiteSection section, SectionRenderContext context);
}

/// <summary>
/// What a section renderer needs from outside itself: the document (to resolve a <c>page:</c> link to a
/// path) and the theme (which it should read through the emitted CSS custom properties rather than
/// inlining, so that a theme change does not require a re-render of anything but the stylesheet).
/// </summary>
public record SectionRenderContext(SiteDocument Document, RenderContext Site)
{
    /// <summary>
    /// Resolves a link value — <c>page:{id}</c>, an absolute URL, or a <c>mailto:</c>/<c>tel:</c> — to
    /// something that belongs in an <c>href</c>. One method, because a section that resolved links itself
    /// would be a section that renders a dead menu when a page's path changes.
    /// </summary>
    public string ResolveHref(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "#";

        if (!value.StartsWith("page:", StringComparison.Ordinal)) return value;

        var page = Document.FindPage(value["page:".Length..]);

        return page is null ? "#" : page.Path;
    }

    /// <summary>
    /// Resolves an image value to a URL. Today every value is already one; the indirection is here so
    /// that the asset store (MASTER_PLAN.md P5) is one method body rather than a change to every
    /// section renderer.
    /// </summary>
    public string? ResolveImage(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
