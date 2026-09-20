using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Rendering;

/// <summary>
/// The whole stylesheet of a published site, derived from its <see cref="SiteTheme"/>.
///
/// One file, and every section's markup styled from it — which is the reason a section renderer emits class
/// names and no inline styles. "Make it warmer" then changes one declaration block and every page with it,
/// and a section added next month inherits a design rather than needing one.
///
/// The variants are computed in OKLCH from the single brand colour instead of being stored: <c>oklch(from
/// var(--brand) …)</c> lets the browser do the arithmetic, so a hover state is always in the same family as
/// the colour it came from and the document never holds four colours that can drift apart. Baseline in every
/// current browser; a 2023-era one falls back to the brand colour flat, which is legible rather than broken.
/// </summary>
public static class ThemeCss
{
    public static string For(SiteTheme theme)
    {
        var (heading, body) = Fonts(theme.Fonts);
        var dark = theme.Mode == ThemeMode.Dark;

        return $$"""
            :root {
              --brand: {{theme.PrimaryColor}};
              --brand-strong: oklch(from var(--brand) calc(l - 0.08) c h);
              --brand-soft: oklch(from var(--brand) calc(l + 0.32) calc(c * 0.35) h);
              --on-brand: oklch(from var(--brand) clamp(0, calc((0.62 - l) * 100), 1) 0 h);

              --page: {{(dark ? "oklch(0.18 0.01 260)" : "oklch(0.995 0.002 260)")}};
              --surface: {{(dark ? "oklch(0.23 0.012 260)" : "oklch(1 0 0)")}};
              --ink: {{(dark ? "oklch(0.96 0.005 260)" : "oklch(0.24 0.012 260)")}};
              --ink-muted: {{(dark ? "oklch(0.76 0.01 260)" : "oklch(0.48 0.012 260)")}};
              --edge: {{(dark ? "oklch(0.32 0.012 260)" : "oklch(0.9 0.006 260)")}};

              --radius: {{Radius(theme.Radius)}};
              --section-space: {{Density(theme.Density)}};
              --font-heading: {{heading}};
              --font-body: {{body}};
              --measure: 68ch;
            }

            *, *::before, *::after { box-sizing: border-box; }
            html { -webkit-text-size-adjust: 100%; }
            body {
              margin: 0;
              background: var(--page);
              color: var(--ink);
              font-family: var(--font-body);
              font-size: 1.0625rem;
              line-height: 1.65;
            }
            h1, h2, h3 { font-family: var(--font-heading); line-height: 1.15; letter-spacing: -0.01em; margin: 0 0 0.6em; }
            h1 { font-size: clamp(2rem, 1.4rem + 2.6vw, 3.5rem); }
            h2 { font-size: clamp(1.5rem, 1.2rem + 1.2vw, 2.25rem); }
            h3 { font-size: 1.125rem; }
            p { margin: 0 0 1em; }
            img { max-width: 100%; height: auto; display: block; }
            a { color: var(--brand-strong); text-decoration-thickness: 1px; text-underline-offset: 2px; }

            /* One horizontal rhythm for every section, and a 1.25rem gutter that a phone needs and a
               desktop does not notice. */
            .wrap { width: min(72rem, 100% - 2.5rem); margin-inline: auto; }
            main > section { padding-block: var(--section-space); }
            .icon { width: 1.5rem; height: 1.5rem; color: var(--brand-strong); }

            .button {
              display: inline-flex; align-items: center; gap: 0.5rem;
              padding: 0.75rem 1.25rem; border-radius: var(--radius);
              font-weight: 600; text-decoration: none; border: 1px solid transparent;
            }
            .button--primary { background: var(--brand); color: var(--on-brand); }
            .button--primary:hover { background: var(--brand-strong); }
            .button--small { padding: 0.45rem 0.9rem; font-size: 0.9375rem; }

            .site-header { border-bottom: 1px solid var(--edge); background: var(--surface); }
            .site-header__inner { display: flex; align-items: center; gap: 1.5rem; padding-block: 1rem; }
            .site-header__name { font-family: var(--font-heading); font-weight: 700; font-size: 1.125rem; color: var(--ink); text-decoration: none; }
            .site-header__name img { max-height: 2rem; width: auto; }
            .site-nav { display: flex; flex-wrap: wrap; gap: 1.25rem; }
            .site-nav a { color: var(--ink-muted); text-decoration: none; font-weight: 500; }
            .site-nav a:hover { color: var(--ink); }
            .site-header__inner > .button { margin-left: auto; }

            .hero__inner { display: grid; gap: clamp(2rem, 5vw, 4rem); align-items: center; }
            .hero--imageRight .hero__inner, .hero--imageLeft .hero__inner { grid-template-columns: 1fr; }
            .hero--imageLeft .hero__media { order: -1; }
            .hero__media img { border-radius: var(--radius); width: 100%; }
            .lede { font-size: 1.1875rem; color: var(--ink-muted); max-width: var(--measure); }

            .prose__body { max-width: var(--measure); }
            .prose--wide .prose__body { max-width: none; }

            .features__grid { list-style: none; margin: 0; padding: 0; display: grid; gap: 1.5rem; }
            .feature { background: var(--surface); border: 1px solid var(--edge); border-radius: var(--radius); padding: 1.5rem; }
            .feature h3 { margin-block: 0.75rem 0.35rem; }
            .feature p { color: var(--ink-muted); margin: 0; }

            .quotes__list { list-style: none; margin: 0; padding: 0; display: grid; gap: 1.5rem; }
            .quote { background: var(--surface); border: 1px solid var(--edge); border-radius: var(--radius); padding: 1.5rem; }
            .quote blockquote { margin: 0 0 1rem; font-size: 1.0625rem; }
            .quote__by { display: flex; align-items: center; gap: 0.75rem; color: var(--ink-muted); font-size: 0.9375rem; }
            .quote__by img { width: 2.5rem; height: 2.5rem; border-radius: 999px; object-fit: cover; }
            .quote__author { font-weight: 600; color: var(--ink); }

            .faq__item { border-bottom: 1px solid var(--edge); padding-block: 1rem; }
            .faq__item summary { font-weight: 600; cursor: pointer; }
            .faq__answer { padding-top: 0.75rem; color: var(--ink-muted); max-width: var(--measure); }

            .cta__inner { text-align: center; }
            .cta--solid { background: var(--brand); color: var(--on-brand); }
            .cta--solid h2, .cta--solid p { color: inherit; }
            .cta--solid .button--primary { background: var(--on-brand); color: var(--brand); }
            .cta--soft { background: var(--brand-soft); }

            .site-footer { border-top: 1px solid var(--edge); background: var(--surface); }
            .site-footer__inner { display: flex; flex-wrap: wrap; gap: 1rem 2rem; align-items: center; justify-content: space-between; padding-block: 2rem; }
            .site-footer__note { color: var(--ink-muted); font-size: 0.9375rem; margin: 0; }

            /* Two breakpoints, not five. Anything more is a design system, and a published site needs to
               look right on a phone and on a laptop. */
            @media (min-width: 48rem) {
              .features__grid--2 { grid-template-columns: repeat(2, 1fr); }
              .features__grid--3, .features__grid--4 { grid-template-columns: repeat(3, 1fr); }
              .quotes__list { grid-template-columns: repeat(2, 1fr); }
            }
            @media (min-width: 64rem) {
              .hero--imageRight .hero__inner, .hero--imageLeft .hero__inner { grid-template-columns: 1.1fr 1fr; }
              .features__grid--4 { grid-template-columns: repeat(4, 1fr); }
            }
            """;
    }

    /// <summary>
    /// The one external request a published page makes, and only when the pairing needs it. Preconnected
    /// rather than merely linked, because a font that arrives after first paint is the visible kind of slow.
    /// </summary>
    public static string FontLink(SiteTheme theme) => theme.Fonts switch
    {
        FontPairing.Modern => Google("Inter:wght@400;600;700"),
        FontPairing.Editorial => Google("Fraunces:wght@500;700", "Inter:wght@400;600"),
        FontPairing.Technical => Google("Space+Grotesk:wght@500;700", "Inter:wght@400;600"),
        FontPairing.Friendly => Google("Nunito:wght@400;600;800"),
        _ => string.Empty
    };

    private static string Google(params string[] families)
    {
        var query = string.Join("&", families.Select(x => $"family={x}"));

        return "<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin>"
            + $"<link rel=\"stylesheet\" href=\"https://fonts.googleapis.com/css2?{query}&display=swap\">";
    }

    private static (string Heading, string Body) Fonts(FontPairing pairing) => pairing switch
    {
        FontPairing.Editorial => ("'Fraunces', Georgia, serif", "'Inter', system-ui, sans-serif"),
        FontPairing.Technical => ("'Space Grotesk', system-ui, sans-serif", "'Inter', system-ui, sans-serif"),
        FontPairing.Friendly => ("'Nunito', system-ui, sans-serif", "'Nunito', system-ui, sans-serif"),
        _ => ("'Inter', system-ui, sans-serif", "'Inter', system-ui, sans-serif")
    };

    private static string Radius(ThemeRadius radius) => radius switch
    {
        ThemeRadius.None => "0",
        ThemeRadius.Small => "0.375rem",
        ThemeRadius.Large => "1.25rem",
        _ => "0.75rem"
    };

    private static string Density(ThemeDensity density) => density switch
    {
        ThemeDensity.Compact => "clamp(2.5rem, 4vw, 3.5rem)",
        ThemeDensity.Spacious => "clamp(4.5rem, 8vw, 8rem)",
        _ => "clamp(3.5rem, 6vw, 6rem)"
    };
}
