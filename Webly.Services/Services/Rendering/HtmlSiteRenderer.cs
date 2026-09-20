using System.Net;
using System.Text;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Rendering;

/// <summary>
/// The renderer: a document in, the files a static host should serve out.
///
/// Plain string building rather than a templating engine, for the same reason the reference project writes
/// its emails that way: there are nine templates in total (the page shell, the stylesheet, six sections and
/// a sitemap), a template engine would add a dependency, a file format and a debugging step, and the output
/// here has to be exactly predictable — a deployment is only safe to retry because the same document always
/// renders the same bytes.
///
/// One file per page as <c>{path}/index.html</c>, so a static host serves clean URLs with no rewrite rules
/// and no per-provider configuration. That is deliberate: rewrite rules are the part of static hosting that
/// differs between providers, and avoiding them is what keeps <c>IDeploymentTarget</c> a thin
/// adapter.
/// </summary>
public class HtmlSiteRenderer(IEnumerable<ISectionRenderer> sectionRenderers) : ISiteRenderer
{
    private readonly Dictionary<SectionType, ISectionRenderer> _sections =
        sectionRenderers.ToDictionary(x => x.Type);

    public RenderedSite Render(SiteDocument document, RenderContext context)
    {
        var files = new List<RenderedFile>();
        var sectionContext = new SectionRenderContext(document, context);

        foreach (var page in document.Pages)
            files.Add(RenderedFile.Html(PathFor(page), RenderPage(document, page, sectionContext, context)));

        files.Add(RenderedFile.Text("styles.css", ThemeCss.For(document.Theme), "text/css; charset=utf-8"));
        files.Add(RenderedFile.Text("sitemap.xml", Sitemap(document, context), "application/xml; charset=utf-8"));
        files.Add(RenderedFile.Text("robots.txt", Robots(context), "text/plain; charset=utf-8"));

        return new RenderedSite(files);
    }

    /// <summary><c>/</c> becomes <c>index.html</c>; <c>/about</c> becomes <c>about/index.html</c>.</summary>
    private static string PathFor(SitePage page) =>
        page.Path == "/" ? "index.html" : $"{page.Path.Trim('/')}/index.html";

    private string RenderPage(
        SiteDocument document,
        SitePage page,
        SectionRenderContext sectionContext,
        RenderContext context)
    {
        var body = new StringBuilder();

        foreach (var section in page.Sections.Where(x => !x.Hidden))
        {
            if (!_sections.TryGetValue(section.Type, out var renderer))
                // Not silent: a section the catalogue allows but nothing can draw is a bug in Webly, and a
                // deployment that quietly omitted a band of somebody's page would hide it.
                throw new InvalidOperationException(
                    $"No renderer is registered for section type '{section.Type}'.");

            body.Append(renderer.Render(section, sectionContext));
        }

        var title = page.Seo.MetaTitle ?? (page.Path == "/" ? context.SiteName : $"{page.Title} · {context.SiteName}");
        var canonical = page.Path == "/" ? context.CanonicalBaseUrl : $"{context.CanonicalBaseUrl}{page.Path}";

        var head = new StringBuilder();
        head.Append("<meta charset=\"utf-8\">");
        head.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        head.Append($"<title>{WebUtility.HtmlEncode(title)}</title>");
        head.Append($"<link rel=\"canonical\" href=\"{SectionMarkup.Attribute(canonical)}\">");

        if (page.Seo.MetaDescription is { Length: > 0 } description)
            head.Append($"<meta name=\"description\" content=\"{SectionMarkup.Attribute(description)}\">");

        if (page.Seo.NoIndex)
            head.Append("<meta name=\"robots\" content=\"noindex,nofollow\">");

        head.Append($"<meta property=\"og:title\" content=\"{SectionMarkup.Attribute(title)}\">");
        head.Append($"<meta property=\"og:url\" content=\"{SectionMarkup.Attribute(canonical)}\">");
        head.Append("<meta property=\"og:type\" content=\"website\">");

        if (page.Seo.OgImage is { Length: > 0 } ogImage)
            head.Append($"<meta property=\"og:image\" content=\"{SectionMarkup.Attribute(ogImage)}\">");

        // Relative, and one level up per path segment, so a page at /a/b still finds it. Absolute URLs
        // would bake the hostname into every page and break the moment a custom domain is connected.
        var stylesheet = context.StylesheetUrl
            ?? (page.Path == "/"
                ? "styles.css"
                : string.Concat(Enumerable.Repeat("../", page.Path.Trim('/').Split('/').Length)) + "styles.css");
        head.Append($"<link rel=\"stylesheet\" href=\"{stylesheet}\">");
        head.Append(ThemeCss.FontLink(document.Theme));

        return $"""
            <!DOCTYPE html>
            <html lang="en" data-theme="{document.Theme.Mode.ToString().ToLowerInvariant()}">
            <head>{head}</head>
            <body>
            {RenderHeader(document, sectionContext)}
            <main>{body}</main>
            {RenderFooter(document, sectionContext)}
            </body>
            </html>
            """;
    }

    private static string RenderHeader(SiteDocument document, SectionRenderContext context)
    {
        var markup = new StringBuilder();

        markup.Append("<header class=\"site-header\"><div class=\"wrap site-header__inner\">");

        var logo = context.ResolveImage(document.Theme.LogoImage);
        markup.Append(logo is null
            ? $"<a class=\"site-header__name\" href=\"/\">{WebUtility.HtmlEncode(context.Site.SiteName)}</a>"
            : $"<a class=\"site-header__name\" href=\"/\"><img src=\"{SectionMarkup.Attribute(logo)}\" alt=\"{SectionMarkup.Attribute(context.Site.SiteName)}\"></a>");

        if (document.Navigation.Header.Count > 0)
        {
            markup.Append("<nav class=\"site-nav\">");

            foreach (var link in document.Navigation.Header)
                markup.Append(Link(link, context));

            markup.Append("</nav>");
        }

        if (document.Navigation.PrimaryAction is { } action)
            markup.Append($"<a class=\"button button--primary button--small\" href=\"{SectionMarkup.Attribute(Href(action, context))}\">{WebUtility.HtmlEncode(action.Label)}</a>");

        markup.Append("</div></header>");

        return markup.ToString();
    }

    private static string RenderFooter(SiteDocument document, SectionRenderContext context)
    {
        var markup = new StringBuilder();

        markup.Append("<footer class=\"site-footer\"><div class=\"wrap site-footer__inner\">");

        if (document.Navigation.Footer.Count > 0)
        {
            markup.Append("<nav class=\"site-nav\">");

            foreach (var link in document.Navigation.Footer)
                markup.Append(Link(link, context));

            markup.Append("</nav>");
        }

        if (document.Navigation.FooterNote is { Length: > 0 } note)
        {
            // The one substitution in the whole renderer. A stored "© 2026" is wrong every January, and
            // nobody edits their footer on New Year's Day.
            var resolved = note.Replace("{year}", DateTime.UtcNow.Year.ToString());
            markup.Append($"<p class=\"site-footer__note\">{WebUtility.HtmlEncode(resolved)}</p>");
        }

        markup.Append("</div></footer>");

        return markup.ToString();
    }

    private static string Link(NavigationLink link, SectionRenderContext context)
    {
        var target = link.NewTab && link.Url is not null ? " target=\"_blank\" rel=\"noopener\"" : string.Empty;

        return $"<a href=\"{SectionMarkup.Attribute(Href(link, context))}\"{target}>{WebUtility.HtmlEncode(link.Label)}</a>";
    }

    private static string Href(NavigationLink link, SectionRenderContext context) =>
        link.PageId is not null ? context.ResolveHref($"page:{link.PageId}") : link.Url ?? "#";

    private static string Sitemap(SiteDocument document, RenderContext context)
    {
        var entries = new StringBuilder();

        // The same switch as the meta tag, from the same field: a page kept out of the index that is still
        // advertised in the sitemap is a contradiction search engines report as an error.
        foreach (var page in document.Pages.Where(x => !x.Seo.NoIndex))
        {
            var url = page.Path == "/" ? context.CanonicalBaseUrl : $"{context.CanonicalBaseUrl}{page.Path}";
            entries.Append($"<url><loc>{WebUtility.HtmlEncode(url)}</loc></url>");
        }

        return $"""<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">{entries}</urlset>""";
    }

    private static string Robots(RenderContext context) =>
        $"""
        User-agent: *
        Allow: /
        Sitemap: {context.CanonicalBaseUrl}/sitemap.xml
        """;
}
