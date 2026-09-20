using System.Text.Json.Nodes;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Sites;

/// <summary>
/// Decides whether a document may be written. Every rule Postgres cannot hold — a jsonb column has no
/// unique index and no check constraint over its contents — lives here, and this runs on the way into
/// <b>every</b> version, whether the change came from the agent, the property editor or a restore.
///
/// The messages are written to be read by the model: a failed tool call hands the problems straight back
/// as its result, and "page path '/about' is already used" is something it can fix on the next attempt,
/// where "validation failed" is not.
/// </summary>
public static class SiteDocumentValidator
{
    /// <summary>The empty list means valid. Everything else is a rejection, one sentence per problem.</summary>
    public static IReadOnlyList<string> Validate(SiteDocument document)
    {
        var problems = new List<string>();

        if (document.Pages.Count == 0)
            problems.Add("A site needs at least one page.");

        if (document.Pages.Count > 0 && document.FindPageByPath("/") is null)
            problems.Add("A site needs a home page, whose path is exactly '/'.");

        foreach (var duplicate in document.Pages.GroupBy(x => x.Path.ToLowerInvariant()).Where(x => x.Count() > 1))
            problems.Add($"Page path '{duplicate.Key}' is used by {duplicate.Count()} pages; paths must be unique.");

        foreach (var duplicate in document.Pages.GroupBy(x => x.Id).Where(x => x.Count() > 1))
            problems.Add($"Page id '{duplicate.Key}' appears more than once.");

        foreach (var page in document.Pages)
            ValidatePage(document, page, problems);

        foreach (var link in document.Navigation.Header.Concat(document.Navigation.Footer)
                     .Append(document.Navigation.PrimaryAction).OfType<NavigationLink>())
            ValidateLink(document, link, problems);

        if (!IsHexColor(document.Theme.PrimaryColor))
            problems.Add($"The theme's primary colour '{document.Theme.PrimaryColor}' is not a hex colour like #2563eb.");

        return problems;
    }

    private static void ValidatePage(SiteDocument document, SitePage page, List<string> problems)
    {
        var where = $"page '{page.Path}'";

        if (string.IsNullOrWhiteSpace(page.Id))
            problems.Add($"A page has no id ({page.Title}).");

        if (!page.Path.StartsWith('/'))
            problems.Add($"Page path '{page.Path}' must start with '/'.");
        else if (page.Path.Length > 1 && page.Path.EndsWith('/'))
            problems.Add($"Page path '{page.Path}' must not end with '/'.");

        if (string.IsNullOrWhiteSpace(page.Title))
            problems.Add($"{where} has no title.");

        foreach (var duplicate in page.Sections.GroupBy(x => x.Id).Where(x => x.Count() > 1))
            problems.Add($"Section id '{duplicate.Key}' appears more than once on {where}.");

        foreach (var section in page.Sections)
            ValidateSection(document, page, section, problems);
    }

    private static void ValidateSection(SiteDocument document, SitePage page, SiteSection section, List<string> problems)
    {
        var where = $"the {section.Type} section on page '{page.Path}'";
        var schema = SectionCatalogue.For(section.Type);

        foreach (var key in section.Props.Select(x => x.Key))
        {
            if (schema.Fields.All(x => x.Name != key))
                problems.Add($"{where} has an unknown field '{key}'. Fields of a {section.Type}: {Names(schema.Fields)}.");
        }

        foreach (var field in schema.Fields)
            ValidateField(document, where, section.Props, field, problems);
    }

    private static void ValidateField(
        SiteDocument document,
        string where,
        JsonObject props,
        SectionFieldSchema field,
        List<string> problems)
    {
        if (!props.TryGetPropertyValue(field.Name, out var value) || value is null)
        {
            if (field.Required)
                problems.Add($"{where} is missing the required field '{field.Name}' — {field.Description}");

            return;
        }

        switch (field.Kind)
        {
            case SectionFieldKind.List:
                if (value is not JsonArray array)
                {
                    problems.Add($"{where}: '{field.Name}' must be a list.");
                    return;
                }

                if (field.Required && array.Count == 0)
                    problems.Add($"{where}: '{field.Name}' cannot be empty — {field.Description}");

                if (field.MaxItems is { } maxItems && array.Count > maxItems)
                    problems.Add($"{where}: '{field.Name}' has {array.Count} items; at most {maxItems} are allowed.");

                for (var index = 0; index < array.Count; index++)
                {
                    if (array[index] is not JsonObject item)
                    {
                        problems.Add($"{where}: '{field.Name}' item {index + 1} is not an object.");
                        continue;
                    }

                    foreach (var key in item.Select(x => x.Key))
                    {
                        if ((field.ItemFields ?? []).All(x => x.Name != key))
                            problems.Add($"{where}: '{field.Name}' item {index + 1} has an unknown field '{key}'. Allowed: {Names(field.ItemFields ?? [])}.");
                    }

                    foreach (var itemField in field.ItemFields ?? [])
                        ValidateField(document, $"{where} ('{field.Name}' item {index + 1})", item, itemField, problems);
                }

                return;

            case SectionFieldKind.Number:
                if (value.GetValueKind() is not System.Text.Json.JsonValueKind.Number)
                    problems.Add($"{where}: '{field.Name}' must be a number.");
                return;

            case SectionFieldKind.Boolean:
                if (value.GetValueKind() is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                    problems.Add($"{where}: '{field.Name}' must be true or false.");
                return;
        }

        // Everything else is carried as a string, and the differences between them are what is allowed
        // *inside* the string.
        if (value.GetValueKind() is not System.Text.Json.JsonValueKind.String)
        {
            problems.Add($"{where}: '{field.Name}' must be text.");
            return;
        }

        var text = value.GetValue<string>();

        if (field.Required && string.IsNullOrWhiteSpace(text))
            problems.Add($"{where}: '{field.Name}' cannot be empty — {field.Description}");

        if (field.MaxLength is { } max && text.Length > max)
            problems.Add($"{where}: '{field.Name}' is {text.Length} characters; at most {max} are allowed.");

        switch (field.Kind)
        {
            case SectionFieldKind.Choice when text.Length > 0 && (field.Choices ?? []).All(x => x != text):
                problems.Add($"{where}: '{field.Name}' is '{text}', which is not one of: {string.Join(", ", field.Choices ?? [])}.");
                break;

            case SectionFieldKind.Color when !IsHexColor(text):
                problems.Add($"{where}: '{field.Name}' must be a hex colour like #2563eb.");
                break;

            case SectionFieldKind.Link when text.Length > 0:
                ValidateLinkTarget(document, $"{where}: '{field.Name}'", text, problems);
                break;

            // Validated as a URL rather than accepted as any string: an image value that is not
            // fetchable is a broken picture on a published page, and the write is the last moment
            // anybody is in a position to say so.
            case SectionFieldKind.Image when text.Length > 0
                && !(Uri.TryCreate(text, UriKind.Absolute, out var image) && image.Scheme == "https"):
                problems.Add($"{where}: '{field.Name}' must be an https image URL.");
                break;
        }
    }

    /// <summary>
    /// A link is either an external absolute URL or <c>page:{pageId}</c> naming one of this document's
    /// own pages. A relative path is deliberately not accepted: a path typed in a field goes stale the
    /// moment the page it names is renamed, and "why does my menu 404" is the bug this rules out.
    /// </summary>
    private static void ValidateLinkTarget(SiteDocument document, string where, string target, List<string> problems)
    {
        if (target.StartsWith("page:", StringComparison.Ordinal))
        {
            var pageId = target["page:".Length..];

            if (document.FindPage(pageId) is null)
                problems.Add($"{where} points at page '{pageId}', which this site does not have.");

            return;
        }

        if (target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            return;

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            problems.Add($"{where} must be an https URL, a mailto:/tel: link, or 'page:{{pageId}}' for one of this site's own pages.");
    }

    private static void ValidateLink(SiteDocument document, NavigationLink link, List<string> problems)
    {
        var where = $"the navigation link '{link.Label}'";

        if (string.IsNullOrWhiteSpace(link.Label))
            problems.Add("A navigation link has no label.");

        // Exactly one subject, the shape used wherever two columns would otherwise have to agree.
        if ((link.PageId is null) == (link.Url is null))
            problems.Add($"{where} must name either one of this site's pages or an external URL, not both and not neither.");

        if (link.PageId is not null && document.FindPage(link.PageId) is null)
            problems.Add($"{where} points at page '{link.PageId}', which this site does not have.");

        if (link.Url is not null)
            ValidateLinkTarget(document, where, link.Url, problems);
    }

    private static string Names(IReadOnlyList<SectionFieldSchema> fields) =>
        string.Join(", ", fields.Select(x => x.Name));

    private static bool IsHexColor(string value) =>
        value.Length == 7
        && value[0] == '#'
        && value[1..].All(Uri.IsHexDigit);
}
