namespace Webly.Services.Services.Rendering.Sections;

/// <summary>
/// The inline SVGs a feature item may use, by name. Inline and from a closed set for the same two reasons
/// the app's own icons are (see <c>shared/icon.ts</c>): a glyph fetched from a CDN is a request, a
/// third-party dependency and a thing that can 404 on somebody's published site, and a closed set is what
/// lets the catalogue offer the names as a choice field instead of a free text box a model can miss.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["check"] = "M20 6 9 17l-5-5",
        ["star"] = "M12 3l2.9 5.9 6.5.9-4.7 4.6 1.1 6.5L12 17.8 6.2 20.9l1.1-6.5L2.6 9.8l6.5-.9z",
        ["clock"] = "M12 7v5l3 2M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z",
        ["shield"] = "M12 3l8 3v6c0 4.4-3.3 8.2-8 9-4.7-.8-8-4.6-8-9V6z",
        ["spark"] = "M12 3v4m0 10v4M3 12h4m10 0h4M6 6l2.5 2.5M15.5 15.5 18 18M18 6l-2.5 2.5M8.5 15.5 6 18",
        ["heart"] = "M12 20s-7-4.4-7-9.5A4 4 0 0 1 12 8a4 4 0 0 1 7 2.5C19 15.6 12 20 12 20z",
        ["chat"] = "M21 12a8 8 0 0 1-8 8H8l-5 3 1.5-5A8 8 0 1 1 21 12z",
        ["tool"] = "M14 7l3 3-8 8H6v-3zM17 4l3 3",
        ["leaf"] = "M5 19c0-8 5-13 14-13 0 9-5 14-13 14H5zM5 19c3-3 6-5 9-6",
        ["truck"] = "M3 7h11v9H3zM14 11h4l3 3v2h-7M6 19a2 2 0 1 0 0-4 2 2 0 0 0 0 4zM17 19a2 2 0 1 0 0-4 2 2 0 0 0 0 4z"
    };

    /// <summary>
    /// The glyph, or nothing at all for an unknown name. Silent rather than throwing: the catalogue
    /// validates the choice on write, so an unknown name here can only come from a document written before
    /// a glyph was renamed, and a missing icon is not a reason to fail somebody's deployment.
    /// </summary>
    public static string Svg(string? name)
    {
        if (name is null || !Paths.TryGetValue(name, out var path)) return string.Empty;

        return "<svg class=\"icon\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" "
            + $"stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\"><path d=\"{path}\"/></svg>";
    }
}
