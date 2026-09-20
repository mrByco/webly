namespace Webly.Data.Models.Sites.Document;

/// <summary>
/// How the whole site looks, in as few numbers as will do it. Every section's template derives its
/// colours, type and spacing from these, so "make it warmer" is one edit that changes every page —
/// the same reason Cookta derives twelve category colours from one hue per category rather than
/// keeping a palette per screen.
///
/// Deliberately not here: per-section colour overrides, arbitrary CSS, a font file upload. Any of
/// them turns "the theme" into a suggestion and makes a site that no longer looks designed the
/// moment the agent touches a second page.
/// </summary>
public class SiteTheme
{
    /// <summary>
    /// The one brand colour, as a hex string. Every accent, button and link derives from it, and the
    /// renderer computes the hover, muted and on-colour variants in OKLCH rather than storing them —
    /// three more colours to keep in step is three more ways for a site to look wrong.
    /// </summary>
    public string PrimaryColor { get; set; } = "#2563eb";

    /// <summary>Page background and text are picked from this pair, light or dark.</summary>
    public ThemeMode Mode { get; set; } = ThemeMode.Light;

    /// <summary>
    /// Font pairing, chosen from a small known-good list rather than a free text field: a Google
    /// Fonts name typed by a model is a 404 at render time, and a site with no font is worse than a
    /// site with the wrong one.
    /// </summary>
    public FontPairing Fonts { get; set; } = FontPairing.Modern;

    /// <summary>Corner rounding, applied to buttons, cards and images alike.</summary>
    public ThemeRadius Radius { get; set; } = ThemeRadius.Medium;

    /// <summary>Vertical rhythm: how much air there is between sections.</summary>
    public ThemeDensity Density { get; set; } = ThemeDensity.Comfortable;

    /// <summary>The logo shown in the header, if any. Falls back to the site name set in type.</summary>
    public string? LogoImage { get; set; }

    /// <summary>The favicon. When absent the renderer emits a letter mark in the primary colour.</summary>
    public string? FaviconImage { get; set; }
}

public enum ThemeMode { Light, Dark }

public enum FontPairing
{
    /// <summary>Inter throughout. The safe default.</summary>
    Modern,

    /// <summary>A serif for headings, a sans for body. Reads as established.</summary>
    Editorial,

    /// <summary>Geometric sans, tight tracking. Reads as a product.</summary>
    Technical,

    /// <summary>Rounded, warm. Reads as a small business.</summary>
    Friendly
}

public enum ThemeRadius { None, Small, Medium, Large }

public enum ThemeDensity { Compact, Comfortable, Spacious }
