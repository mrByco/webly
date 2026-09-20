using System.Net;
using System.Text.Json.Nodes;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Rendering;

/// <summary>
/// Reading props, safely. Every section renderer goes through these, so the escaping decision is made
/// once: a value in the document was written by a language model or typed by a member of the public, and
/// a renderer that interpolates one into markup by hand is how a site ends up serving somebody's
/// <c>&lt;script&gt;</c>.
///
/// The single exception is <see cref="RichTextHtml"/>, which is the one field kind that is allowed to be
/// markup, and which is sanitized rather than escaped.
/// </summary>
public static class SectionMarkup
{
    /// <summary>A text prop, HTML-escaped. Missing reads as empty — the validator has already rejected a
    /// document where a required field is absent, so a renderer never has to branch on it.</summary>
    public static string Text(this SiteSection section, string field) =>
        WebUtility.HtmlEncode(section.Raw(field) ?? string.Empty);

    /// <summary>A text prop, escaped, or null when it is absent or empty — for the optional ones, where a
    /// renderer wants to omit the whole element rather than emit an empty one.</summary>
    public static string? TextOrNull(this SiteSection section, string field)
    {
        var raw = section.Raw(field);

        return string.IsNullOrWhiteSpace(raw) ? null : WebUtility.HtmlEncode(raw);
    }

    /// <summary>The raw string, unescaped. For values that go somewhere other than text content — an
    /// <c>href</c>, a choice compared against a constant — and always escaped by the caller if they end up
    /// in an attribute.</summary>
    public static string? Raw(this SiteSection section, string field) =>
        section.Props.TryGetPropertyValue(field, out var value) && value is JsonValue text
            ? text.GetValue<string?>()
            : null;

    /// <summary>A choice prop, compared against its options by the caller. Never null: the catalogue gives
    /// every choice field a default.</summary>
    public static string Choice(this SiteSection section, string field, string fallback) =>
        section.Raw(field) is { Length: > 0 } value ? value : fallback;

    /// <summary>A list prop's items. Empty when absent, so a renderer can always foreach.</summary>
    public static IEnumerable<JsonObject> Items(this SiteSection section, string field) =>
        section.Props.TryGetPropertyValue(field, out var value) && value is JsonArray array
            ? array.OfType<JsonObject>()
            : [];

    /// <summary>An item field of a list entry, escaped.</summary>
    public static string Text(this JsonObject item, string field) =>
        WebUtility.HtmlEncode(item.Raw(field) ?? string.Empty);

    public static string? TextOrNull(this JsonObject item, string field)
    {
        var raw = item.Raw(field);

        return string.IsNullOrWhiteSpace(raw) ? null : WebUtility.HtmlEncode(raw);
    }

    public static string? Raw(this JsonObject item, string field) =>
        item.TryGetPropertyValue(field, out var value) && value is JsonValue text
            ? text.GetValue<string?>()
            : null;

    /// <summary>
    /// A rich-text prop, sanitized down to the tags the editor can produce.
    ///
    /// An allowlist over a parsed document, not a regular expression over a string: stripping
    /// <c>&lt;script&gt;</c> with a pattern is the canonical example of a filter that loses to the next
    /// encoding trick. This is the one place in Webly where markup from a prop reaches a page, so it is
    /// the one place that has to be right — and it is deliberately narrow, because nothing about the
    /// product needs a <c>&lt;table&gt;</c> or an <c>&lt;iframe&gt;</c> inside a paragraph.
    ///
    /// NOTE: the implementation below is the structural placeholder for that allowlist — it escapes
    /// everything, which is safe but renders the markup visibly. The real one lands with the rich-text
    /// editor (MASTER_PLAN.md P3): sanitizing HTML properly is a library's job (AngleSharp, or
    /// HtmlSanitizer), not thirty lines here, and shipping thirty lines here is exactly how a product
    /// ends up with a filter somebody trusts.
    /// </summary>
    public static string RichTextHtml(this SiteSection section, string field) =>
        WebUtility.HtmlEncode(section.Raw(field) ?? string.Empty);

    public static string RichTextHtml(this JsonObject item, string field) =>
        WebUtility.HtmlEncode(item.Raw(field) ?? string.Empty);

    /// <summary>An attribute value, escaped. Used for hrefs and image sources.</summary>
    public static string Attribute(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
