using System.Text;
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
    /// The template's tree with this look written into it.
    ///
    /// <b>Three numbers are replaced; the file is not generated.</b> Its prose — what a hue is, that "make it
    /// green" means editing this file — belongs next to it in the template where a person edits it, not in a
    /// C# string that would drift from it silently. A file that has been renamed or reshaped so the numbers are
    /// no longer there leaves the tree exactly as it is: a site with the default look is a small
    /// disappointment, and a site whose stylesheet we corrupted is a broken website.
    /// </summary>
    public static WorkspaceTree Applied(WorkspaceTree template, SiteLook look)
    {
        var file = template.Find(Path);

        if (file is null) return template;

        var css = Encoding.UTF8.GetString(file.Content);
        var rewritten = Hue().Replace(css, $"--brand-hue: {look.Hue};");

        rewritten = Chroma().Replace(
            rewritten, $"--brand-chroma: {look.Chroma.ToString(System.Globalization.CultureInfo.InvariantCulture)};");

        rewritten = Radius().Replace(rewritten, $"--card-radius: {look.Radius};");

        if (rewritten == css) return template;

        return new WorkspaceTree(
        [
            .. template.Files.Select(x => x.Path == Path ? WorkspaceFile.Text(Path, rewritten) : x)
        ]);
    }

    [GeneratedRegex(@"--brand-hue:\s*[\d.]+;")]
    private static partial Regex Hue();

    [GeneratedRegex(@"--brand-chroma:\s*[\d.]+;")]
    private static partial Regex Chroma();

    [GeneratedRegex(@"--card-radius:\s*[^;]+;")]
    private static partial Regex Radius();
}
