using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webly.Data.Models.Sites.Document;
using Webly.Services.Services.Sites;

namespace Webly.Services.Agent.Tools;

/// <summary>
/// What the agent may do to a site. <b>The tool list is the permission model</b> — there is no other check, and
/// that is why what is absent matters as much as what is here.
///
/// Absent on purpose: publishing (a person decides when their site goes live, and a deploy spends provider quota),
/// anything to do with domains or billing, and deleting the site. Adding one of those is a method plus a review,
/// and the diff shows it.
///
/// Every method returns prose, including its failures. A tool result is the model's only feedback channel, so
/// "page path '/about' is already used" is worth more than a status code — it is what lets the model fix its own
/// mistake on the next call instead of reporting to the person that something went wrong.
/// </summary>
public class SiteToolkit(SiteEditSession session)
{
    [Description("Read the whole site: its pages, the sections on each page with their ids and current content, the menu and the theme. Call this first in a turn unless you already know the structure.")]
    public async Task<string> ReadSite(CancellationToken cancellationToken = default)
    {
        var draft = await session.GetDraftAsync(cancellationToken);
        var document = draft.Document;
        var text = new StringBuilder();

        text.AppendLine($"Theme: primary {document.Theme.PrimaryColor}, {document.Theme.Mode} mode, {document.Theme.Fonts} fonts, {document.Theme.Radius} corners, {document.Theme.Density} spacing.");
        text.AppendLine($"Menu: {DescribeNavigation(document)}");
        text.AppendLine();

        foreach (var page in document.Pages)
        {
            text.AppendLine($"Page {page.Path} (id {page.Id}) — \"{page.Title}\"");

            if (page.Sections.Count == 0)
                text.AppendLine("  (no sections)");

            foreach (var section in page.Sections)
            {
                text.AppendLine($"  {section.Type} (id {section.Id}){(section.Hidden ? " [hidden]" : string.Empty)}");
                text.AppendLine($"    {section.Props.ToJsonString()}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    [Description("Add a page. The path is normalised: 'about', '/about' and 'About' all become '/about'. Returns the new page's id.")]
    public async Task<string> AddPage(
        [Description("The URL path, e.g. 'about' or 'prices'.")] string path,
        [Description("The page's name, as it should appear in the menu and the browser tab.")] string title,
        CancellationToken cancellationToken = default)
    {
        var draft = await session.GetDraftAsync(cancellationToken);
        var result = draft.AddPage(path, title);

        return result.Succeeded
            ? $"Added the page {path} with id {result.Id}. It has no sections yet — add some."
            : result.Message;
    }

    [Description("Change a page's path, title or SEO metadata. Only the arguments you pass are changed.")]
    public async Task<string> UpdatePage(
        string pageId,
        string? path = null,
        string? title = null,
        [Description("The <title> and search-result heading, when it should differ from the page's name.")] string? metaTitle = null,
        [Description("The search-result description. One or two sentences, written for someone deciding whether to click.")] string? metaDescription = null,
        CancellationToken cancellationToken = default)
    {
        var draft = await session.GetDraftAsync(cancellationToken);
        var page = draft.Document.FindPage(pageId);

        if (page is null) return $"This site has no page with id '{pageId}'. Call ReadSite to see its pages.";

        // Patched onto the current SEO rather than replaced, so setting a description does not clear a title.
        var seo = metaTitle is null && metaDescription is null
            ? null
            : new PageSeo
            {
                MetaTitle = metaTitle ?? page.Seo.MetaTitle,
                MetaDescription = metaDescription ?? page.Seo.MetaDescription,
                OgImage = page.Seo.OgImage,
                NoIndex = page.Seo.NoIndex
            };

        return draft.UpdatePage(pageId, path, title, seo).Message;
    }

    [Description("Delete a page. The home page cannot be deleted.")]
    public async Task<string> RemovePage(string pageId, CancellationToken cancellationToken = default) =>
        (await session.GetDraftAsync(cancellationToken)).RemovePage(pageId).Message;

    [Description("Add a section to a page. Props is a JSON object of the section type's fields; anything you leave out gets its default, so you can add a section and fill it in afterwards. Returns the new section's id.")]
    public async Task<string> AddSection(
        string pageId,
        [Description("One of the section types described in your instructions.")] SectionType type,
        [Description("A JSON object of this section type's fields, e.g. {\"headline\":\"...\",\"subheadline\":\"...\"}.")] string props = "{}",
        [Description("Where on the page, counting from 0. Omit to append at the bottom.")] int? index = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParse(props, out var parsed, out var parseError)) return parseError;

        var draft = await session.GetDraftAsync(cancellationToken);
        var result = draft.AddSection(pageId, type, parsed, index);

        return result.Succeeded
            ? $"Added a {type} section with id {result.Id}."
            : result.Message;
    }

    [Description("Change fields on a section. This is a patch: fields you pass are replaced, fields you omit are left alone, and a field set to null is cleared. Never resend fields you did not mean to change.")]
    public async Task<string> UpdateSection(
        string sectionId,
        [Description("A JSON object of just the fields to change.")] string props,
        CancellationToken cancellationToken = default)
    {
        if (!TryParse(props, out var parsed, out var parseError)) return parseError;

        var draft = await session.GetDraftAsync(cancellationToken);

        return draft.UpdateSection(sectionId, parsed).Message;
    }

    [Description("Move a section up or down its page. The index counts from 0.")]
    public async Task<string> MoveSection(string sectionId, int toIndex, CancellationToken cancellationToken = default) =>
        (await session.GetDraftAsync(cancellationToken)).MoveSection(sectionId, toIndex).Message;

    [Description("Delete a section. If the person might want it back, hide it instead.")]
    public async Task<string> RemoveSection(string sectionId, CancellationToken cancellationToken = default) =>
        (await session.GetDraftAsync(cancellationToken)).RemoveSection(sectionId).Message;

    [Description("Hide or show a section without deleting it.")]
    public async Task<string> SetSectionHidden(string sectionId, bool hidden, CancellationToken cancellationToken = default) =>
        (await session.GetDraftAsync(cancellationToken)).SetSectionHidden(sectionId, hidden).Message;

    [Description("Change how the whole site looks. Only the arguments you pass are changed. One brand colour drives every accent, so pick the customer's real colour if they have one.")]
    public async Task<string> SetTheme(
        [Description("A hex colour like #1d4ed8.")] string? primaryColor = null,
        ThemeMode? mode = null,
        FontPairing? fonts = null,
        ThemeRadius? radius = null,
        ThemeDensity? density = null,
        CancellationToken cancellationToken = default) =>
        (await session.GetDraftAsync(cancellationToken)).SetTheme(primaryColor, mode, fonts, radius, density).Message;

    [Description("Replace the menu. Every link names either one of this site's pages (by id) or an external URL — never a path, so that renaming a page cannot break the menu.")]
    public async Task<string> SetNavigation(
        [Description("""A JSON object: {"header":[{"label":"Home","pageId":"..."}],"footer":[],"primaryAction":{"label":"Call us","url":"tel:+15551234567"},"footerNote":"© {year} Name"}""")] string navigation,
        CancellationToken cancellationToken = default)
    {
        SiteNavigation? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<SiteNavigation>(navigation, SiteDocument.SerializerOptions);
        }
        catch (JsonException exception)
        {
            return $"That is not valid JSON for a menu: {exception.Message}";
        }

        if (parsed is null) return "The menu cannot be empty JSON. Pass an object with header, footer and optionally primaryAction.";

        return (await session.GetDraftAsync(cancellationToken)).SetNavigation(parsed).Message;
    }

    /// <summary>
    /// Props arrive as a JSON string rather than a typed parameter, because the shape differs per section type and
    /// the schema that describes it is data (see <c>SectionCatalogue</c>) rather than a class. The cost is this
    /// parse and its error message; the benefit is that adding a section type needs no new tool.
    /// </summary>
    private static bool TryParse(string json, out JsonObject parsed, out string error)
    {
        parsed = new JsonObject();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json)) return true;

        try
        {
            parsed = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

            return true;
        }
        catch (JsonException exception)
        {
            error = $"That is not a valid JSON object: {exception.Message}. Pass fields like {{\"headline\":\"...\"}}.";

            return false;
        }
        catch (InvalidOperationException)
        {
            error = "Props must be a JSON object, not a list or a bare value.";

            return false;
        }
    }

    private static string DescribeNavigation(SiteDocument document)
    {
        string Describe(NavigationLink link) =>
            $"\"{link.Label}\" → {(link.PageId is not null ? $"page {link.PageId}" : link.Url)}";

        var header = document.Navigation.Header.Select(Describe);
        var footer = document.Navigation.Footer.Select(Describe);

        return $"header [{string.Join(", ", header)}], footer [{string.Join(", ", footer)}]"
            + (document.Navigation.PrimaryAction is { } action ? $", button {Describe(action)}" : string.Empty);
    }
}
