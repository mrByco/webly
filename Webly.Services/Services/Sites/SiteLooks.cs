using System.Text.RegularExpressions;
using Webly.Services.Services.Repositories;

namespace Webly.Services.Services.Sites;

/// <summary>
/// The colour and shape a new site starts with.
///
/// <b>One template, several looks.</b> Every site here begins as the same Next.js project, which is right —
/// the agent's job is easier when the first thing it does is an edit rather than a creation, and four
/// templates would be four copies of every convention to keep in step the next time a rule changes. But one
/// template also means every site that nobody has styled yet is the same indigo page, and a product whose
/// customers' websites look alike is a template mill.
///
/// So the variety is three numbers rather than three projects. The template derives its whole palette from an
/// OKLCH hue and a chroma — neutrals included, which is what stops a brand colour looking dropped onto
/// somebody else's site — and its cards from one radius. Changing those changes the site's character without
/// touching a line of its markup, and "make it green" stays an edit to one file the agent can make.
/// </summary>
/// <param name="Name">For the log and for the comment written into the site, never a stored column.</param>
/// <param name="Hue">An OKLCH angle: 25 terracotta, 145 green, 220 sea, 268 indigo, 330 plum.</param>
/// <param name="Chroma">How much colour there is at all. 0.04 reads as ink; 0.19 as a confident brand.</param>
/// <param name="Radius">Card corners, as a CSS length. Sharp reads as serious, round as friendly.</param>
public record SiteLook(string Name, int Hue, double Chroma, string Radius);

public static partial class SiteLooks
{
    /// <summary>The file the template keeps them in. Webly rewrites its numbers and nothing else.</summary>
    public const string Path = "src/app/look.css";

    /// <summary>
    /// The site's tab icon, which carries the brand colour as a literal.
    ///
    /// It has to: an SVG the browser loads as a file cannot read the page's CSS variables, so a hue written in
    /// one place cannot reach it. That is the one duplication in this design, and it is rewritten here so that a
    /// terracotta site does not get an indigo tile.
    /// </summary>
    public const string IconPath = "src/app/icon.svg";

    /// <summary>
    /// The card a shared link shows, which carries the brand colour as a hex.
    ///
    /// The third and last copy of it, and the one that is written differently: it is drawn at build time by a
    /// renderer with no CSS engine, which refuses <c>oklch()</c> — see <see cref="Oklch"/>.
    /// </summary>
    public const string CardPath = "src/app/opengraph-image.tsx";

    /// <summary>
    /// The lightness the template uses everywhere the brand colour is a literal.
    ///
    /// Not part of a look: it is what keeps white text readable on the colour and the mark legible at sixteen
    /// pixels, and a look that moved it would be choosing a different problem.
    /// </summary>
    private const double BrandLightness = 0.52;

    /// <summary>
    /// Five, chosen to be different in kind rather than in shade: two of them are warm, one is quiet enough to
    /// read as black, and the radii run from nearly square to obviously friendly. A palette generator would
    /// give more variety and less taste — these are five that were looked at.
    /// </summary>
    public static readonly IReadOnlyList<SiteLook> All =
    [
        new("indigo", 268, 0.19, "0.875rem"),
        new("terracotta", 45, 0.15, "1.5rem"),
        new("forest", 150, 0.12, "0.5rem"),
        new("sea", 220, 0.15, "1rem"),
        new("ink", 285, 0.04, "0.25rem"),
    ];

    /// <summary>
    /// One of them, at random.
    /// </summary>
    /// <remarks>
    /// Random rather than asked for, and that is a product decision worth stating: the first screen of this
    /// product asks for a name and nothing else, and a palette picker before anybody has typed a sentence is a
    /// decision about a business that does not exist yet. Somebody who wants another says so in the chat, which
    /// is the whole premise — and until they do, at least their site is not the same page as everyone else's.
    /// </remarks>
    public static SiteLook Choose() => All[Random.Shared.Next(All.Count)];

    /// <summary>
    /// The template's tree with this look written into it: three numbers in the stylesheet, and the same colour
    /// in the icon, which cannot read them.
    ///
    /// <b>Numbers are replaced; neither file is generated.</b> Their prose — what a hue is, that "make it green"
    /// means editing this file, that the icon should be replaced when the business has a mark of its own —
    /// belongs next to them in the template where a person edits them, not in a C# string that would drift from
    /// them silently.
    /// </summary>
    public static WorkspaceTree Applied(WorkspaceTree template, SiteLook look)
    {
        var tree = TemplateFile.Rewritten(template, Path, css => Stylesheet(css, look));

        tree = TemplateFile.Rewritten(tree, IconPath, svg => Icon(svg, look));

        return TemplateFile.Rewritten(tree, CardPath, card => Card(card, look));
    }

    /// <summary>
    /// The look written into the stylesheet everything else derives from.
    /// </summary>
    private static string Stylesheet(string css, SiteLook look)
    {
        var rewritten = Hue().Replace(css, $"--brand-hue: {look.Hue};");

        rewritten = Chroma().Replace(rewritten, $"--brand-chroma: {Number(look.Chroma)};");

        return Radius().Replace(rewritten, $"--card-radius: {look.Radius};");
    }

    /// <summary>
    /// The same colour written into the icon, where it cannot be a variable. The lightness the template chose is
    /// kept: it is what makes the mark readable at sixteen pixels, and it is not part of the look.
    /// </summary>
    private static string Icon(string svg, SiteLook look) =>
        IconFill().Replace(svg, m => $"oklch({m.Groups[1].Value} {Number(look.Chroma)} {look.Hue})");

    /// <summary>
    /// The same colour again, as a hex, for the share card. See <see cref="CardPath"/> for why it is not an
    /// <c>oklch()</c> like the other two.
    /// </summary>
    private static string Card(string source, SiteLook look) =>
        CardBrand().Replace(source, $"const brand = '{Oklch.ToHex(BrandLightness, look.Chroma, look.Hue)}';");

    /// <summary>Invariant, because a chroma written as <c>0,19</c> is a stylesheet that does not parse.</summary>
    private static string Number(double value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex(@"--brand-hue:\s*[\d.]+;")]
    private static partial Regex Hue();

    [GeneratedRegex(@"--brand-chroma:\s*[\d.]+;")]
    private static partial Regex Chroma();

    [GeneratedRegex(@"--card-radius:\s*[^;]+;")]
    private static partial Regex Radius();

    /// <summary>The icon's own colour, with its lightness captured so the rewrite keeps it.</summary>
    [GeneratedRegex(@"oklch\(\s*([\d.]+%)\s+[\d.]+\s+[\d.]+\s*\)")]
    private static partial Regex IconFill();

    /// <summary>
    /// The share card's brand colour, the one hex in the project.
    ///
    /// The <i>name</i> is what is matched, not "a background that is a hex": the card has white in it too, and
    /// matching by role painted the rule brand-coloured on a brand background — an element still on the card
    /// and impossible to see. Found by publishing a terracotta site and looking at its card.
    /// </summary>
    [GeneratedRegex(@"const brand = '#[0-9a-fA-F]{6}';")]
    private static partial Regex CardBrand();
}
