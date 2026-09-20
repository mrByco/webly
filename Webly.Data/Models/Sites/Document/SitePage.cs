namespace Webly.Data.Models.Sites.Document;

/// <summary>One page of a site: a path, its SEO metadata, and an ordered list of sections.</summary>
public class SitePage
{
    /// <summary>
    /// Stable identity for the page across versions and renames, so a navigation link, a redirect or
    /// an agent tool call can name a page without depending on its path. A nanoid, minted by the use
    /// case that adds the page — the document is not an entity, so nothing stamps it.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The URL path, always starting with <c>/</c> and never ending with one (the home page is the
    /// single exception: it is exactly <c>/</c>). Unique within a document — enforced by the
    /// validator, since jsonb cannot hold a unique index.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>Shown in the editor's page list and used as the default <c>&lt;title&gt;</c>.</summary>
    public required string Title { get; set; }

    public PageSeo Seo { get; set; } = new();

    /// <summary>
    /// The sections, top to bottom. The array's order <i>is</i> the order; there is no sort field to
    /// disagree with it. Moving a section is a list reorder.
    /// </summary>
    public List<SiteSection> Sections { get; set; } = [];

    public SiteSection? FindSection(string sectionId) => Sections.FirstOrDefault(x => x.Id == sectionId);
}
