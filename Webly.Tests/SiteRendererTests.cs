using System.Text;
using System.Text.Json.Nodes;
using Webly.Data.Models.Sites.Document;
using Webly.Services.Services.Rendering;
using Webly.Services.Services.Rendering.Sections;
using Webly.Services.Services.Sites;

namespace Webly.Tests;

/// <summary>
/// What a published site actually contains. These tests matter more than most: the renderer's output is
/// served to the public under a customer's own domain, and nothing downstream reviews it.
/// </summary>
public class SiteRendererTests
{
    private static readonly HtmlSiteRenderer Renderer = new(
    [
        new HeroSectionRenderer(),
        new RichTextSectionRenderer(),
        new FeatureGridSectionRenderer(),
        new TestimonialsSectionRenderer(),
        new FaqSectionRenderer(),
        new CtaSectionRenderer()
    ]);

    private static readonly RenderContext Context = new("Kovacs Bakery", "https://bakery.example.com");

    private static string Text(RenderedSite site, string path) =>
        Encoding.UTF8.GetString(site.Files.First(x => x.Path == path).Content);

    [Test]
    public void The_starter_template_renders_a_home_page_a_stylesheet_and_a_sitemap()
    {
        var site = Renderer.Render(StarterTemplate.For("Kovacs Bakery"), Context);

        Assert.Multiple(() =>
        {
            Assert.That(site.Files.Select(x => x.Path), Is.SupersetOf(new[] { "index.html", "styles.css", "sitemap.xml", "robots.txt" }));
            Assert.That(Text(site, "index.html"), Does.Contain("<!DOCTYPE html>"));
            Assert.That(Text(site, "index.html"), Does.Contain("Kovacs Bakery"));
            Assert.That(Text(site, "sitemap.xml"), Does.Contain("https://bakery.example.com"));
        });
    }

    /// <summary>
    /// A page is a directory with an index, so a static host serves clean URLs with no rewrite rules —
    /// which is what keeps the deployment target a thin adapter.
    /// </summary>
    [Test]
    public void A_second_page_renders_as_a_directory_index()
    {
        var draft = SiteDraft.From(StarterTemplate.For("Kovacs Bakery"));
        draft.AddPage("about", "About");

        var site = Renderer.Render(draft.Document, Context);

        Assert.That(site.Files.Select(x => x.Path), Does.Contain("about/index.html"));
    }

    /// <summary>
    /// The values in a document were written by a language model and typed by a member of the public.
    /// Neither is a reason to trust a string into markup.
    /// </summary>
    [Test]
    public void Text_from_a_section_is_escaped()
    {
        var draft = SiteDraft.From(StarterTemplate.For("Test"));
        var hero = draft.Document.Pages[0].Sections.First(x => x.Type == SectionType.Hero);

        var edit = draft.UpdateSection(hero.Id, new JsonObject { ["headline"] = "<script>alert(1)</script>" });
        var html = Text(Renderer.Render(draft.Document, Context), "index.html");

        Assert.Multiple(() =>
        {
            Assert.That(edit.Succeeded, Is.True, edit.Message);
            Assert.That(html, Does.Not.Contain("<script>alert"));
            Assert.That(html, Does.Contain("&lt;script&gt;"));
        });
    }

    [Test]
    public void A_hidden_section_is_not_rendered()
    {
        var draft = SiteDraft.From(StarterTemplate.For("Test"));
        var features = draft.Document.Pages[0].Sections.First(x => x.Type == SectionType.FeatureGrid);

        draft.SetSectionHidden(features.Id, true);

        Assert.That(Text(Renderer.Render(draft.Document, Context), "index.html"), Does.Not.Contain("class=\"features\""));
    }

    /// <summary>
    /// Both effects of one switch: the meta tag and the sitemap. A page kept out of the index that is
    /// still advertised in the sitemap is a contradiction search engines report as an error.
    /// </summary>
    [Test]
    public void A_noindex_page_is_marked_and_left_out_of_the_sitemap()
    {
        var draft = SiteDraft.From(StarterTemplate.For("Test"));
        var added = draft.AddPage("internal", "Internal");

        draft.UpdatePage(added.Id!, null, null, new PageSeo { NoIndex = true });

        var site = Renderer.Render(draft.Document, Context);

        Assert.Multiple(() =>
        {
            Assert.That(Text(site, "internal/index.html"), Does.Contain("noindex"));
            Assert.That(Text(site, "sitemap.xml"), Does.Not.Contain("/internal"));
        });
    }

    /// <summary>
    /// The same document and context always render the same bytes. That is what makes a failed deployment
    /// safe to retry, and it is why the renderer may not read a clock or a random number.
    /// </summary>
    [Test]
    public void Rendering_is_deterministic()
    {
        var document = StarterTemplate.For("Kovacs Bakery");

        var first = Text(Renderer.Render(document, Context), "index.html");
        var second = Text(Renderer.Render(document, Context), "index.html");

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public void The_stylesheet_follows_the_theme()
    {
        var draft = SiteDraft.From(StarterTemplate.For("Test"));
        draft.SetTheme(primaryColor: "#ff0055", mode: ThemeMode.Dark);

        var css = Text(Renderer.Render(draft.Document, Context), "styles.css");

        Assert.That(css, Does.Contain("#ff0055"));
    }

    /// <summary>
    /// The preview serves every page from one URL, so its stylesheet link cannot be the relative one a
    /// deployed page uses. See <c>RenderContext.StylesheetUrl</c>.
    /// </summary>
    [Test]
    public void A_stylesheet_override_replaces_the_relative_link()
    {
        var draft = SiteDraft.From(StarterTemplate.For("Test"));
        draft.AddPage("about", "About");

        var overridden = new RenderContext(Context.SiteName, Context.CanonicalBaseUrl) { StylesheetUrl = "/api/sites/abc/preview.css" };

        var relative = Text(Renderer.Render(draft.Document, Context), "about/index.html");
        var absolute = Text(Renderer.Render(draft.Document, overridden), "about/index.html");

        Assert.Multiple(() =>
        {
            Assert.That(relative, Does.Contain("../styles.css"));
            Assert.That(absolute, Does.Contain("/api/sites/abc/preview.css"));
        });
    }
}
