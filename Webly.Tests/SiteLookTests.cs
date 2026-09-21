using System.Text;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sites;

namespace Webly.Tests;

/// <summary>
/// The look a new site starts with: three numbers in one stylesheet and the same colour in the tab icon,
/// rewritten in the template's tree on the way to the first commit.
///
/// What is worth testing is not which colours were chosen but that the rewrite cannot damage a site. It runs
/// on every site anybody ever creates, before anything has looked at the result, and the file it edits is the
/// one that makes the whole thing render.
/// </summary>
public class SiteLookTests
{
    /// <summary>The template's own file, as close to the real one as a test should copy.</summary>
    private const string LookCss =
        """
        /* This site's look. */
        :root {
          --brand-hue: 268;
          --brand-chroma: 0.19;
          --card-radius: 0.875rem;
        }
        """;

    /// <summary>The template's icon, which carries the colour as a literal because a file cannot read a variable.</summary>
    private const string IconSvg =
        """
        <!-- This site's icon. -->
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64">
          <rect width="64" height="64" rx="14" fill="oklch(52% 0.19 268)" />
          <path d="M20 40c0-9 5.5-16 12-16s12 7 12 16" fill="none" stroke="#ffffff" stroke-width="6" />
        </svg>
        """;

    private static WorkspaceTree Template(params (string Path, string Content)[] extra) =>
        new([
            WorkspaceFile.Text(SiteLooks.Path, LookCss),
            .. extra.Select(x => WorkspaceFile.Text(x.Path, x.Content)),
        ]);

    [Test]
    public void Every_look_is_written_as_css_a_browser_can_read()
    {
        Assert.Multiple(() =>
        {
            foreach (var look in SiteLooks.All)
            {
                var applied = SiteLooks.Applied(Template(), look);
                var css = applied.Find(SiteLooks.Path)!.AsText();

                Assert.That(css, Does.Contain($"--brand-hue: {look.Hue};"), look.Name);
                Assert.That(css, Does.Contain($"--card-radius: {look.Radius};"), look.Name);

                // The one that a culture can break: a machine with a comma decimal separator would write
                // "0,15", which is not a number in CSS — the palette silently falls back to unstyled and
                // nobody finds out until a customer's site is beige.
                Assert.That(css, Does.Contain($"--brand-chroma: {look.Chroma.ToString(System.Globalization.CultureInfo.InvariantCulture)};"), look.Name);
                Assert.That(css, Does.Not.Contain(","), look.Name);

                // And the file is still the template's file, with its prose intact.
                Assert.That(css, Does.StartWith("/* This site's look. */"), look.Name);
            }
        });
    }

    /// <summary>
    /// The icon follows the stylesheet. It has to be rewritten separately — an SVG the browser fetches as a file
    /// cannot read the page's CSS variables — and the failure it prevents is a terracotta site whose tab shows an
    /// indigo tile, which is the kind of wrong that only a person looking at a browser ever notices.
    /// </summary>
    [Test]
    public void The_icon_is_the_same_colour_as_the_rest_of_the_site()
    {
        Assert.Multiple(() =>
        {
            foreach (var look in SiteLooks.All)
            {
                var applied = SiteLooks.Applied(Template((SiteLooks.IconPath, IconSvg)), look);
                var svg = applied.Find(SiteLooks.IconPath)!.AsText();

                var chroma = look.Chroma.ToString(System.Globalization.CultureInfo.InvariantCulture);

                Assert.That(svg, Does.Contain($"oklch(52% {chroma} {look.Hue})"), look.Name);

                // The lightness is the template's, not the look's: it is what keeps the mark legible at sixteen
                // pixels, and a rewrite that dropped it would produce colours nobody chose.
                Assert.That(svg, Does.Contain("rx=\"14\""), look.Name);
                Assert.That(svg, Does.Contain("stroke=\"#ffffff\""), look.Name);
                Assert.That(svg, Does.StartWith("<!-- This site's icon. -->"), look.Name);
            }
        });
    }

    [Test]
    public void A_template_with_no_icon_still_gets_its_stylesheet()
    {
        // Two files, two independent rewrites: an older template that has no icon must not lose its look
        // because of a file it never had.
        var css = SiteLooks.Applied(Template(), SiteLooks.All[1]).Find(SiteLooks.Path)!.AsText();

        Assert.That(css, Does.Contain($"--brand-hue: {SiteLooks.All[1].Hue};"));
    }

    [Test]
    public void Nothing_but_the_numbers_changes()
    {
        var page = ("src/app/page.tsx", "export default () => null;\n");
        var applied = SiteLooks.Applied(Template(page), SiteLooks.All[1]);

        Assert.Multiple(() =>
        {
            Assert.That(applied.Files, Has.Count.EqualTo(2));
            Assert.That(applied.Find(page.Item1)!.AsText(), Is.EqualTo(page.Item2), "the site itself is untouched");
        });
    }

    [Test]
    public void A_template_that_has_moved_the_file_is_left_exactly_as_it_is()
    {
        // The failure this exists for: a rewrite that half-matched, or that wrote a file where none was
        // expected, would ship a broken stylesheet to every site created after it. A site with the default
        // look is a small disappointment; a site whose CSS we corrupted is a broken website.
        var without = new WorkspaceTree([WorkspaceFile.Text("src/app/globals.css", "@import 'tailwindcss';\n")]);

        Assert.That(SiteLooks.Applied(without, SiteLooks.All[2]), Is.SameAs(without));

        var reshaped = new WorkspaceTree([WorkspaceFile.Text(SiteLooks.Path, ":root { --colour: red; }\n")]);

        Assert.That(SiteLooks.Applied(reshaped, SiteLooks.All[3]), Is.SameAs(reshaped));
    }

    [Test]
    public void The_chosen_look_is_one_of_the_ones_on_the_list()
    {
        // Twenty draws rather than one, because the interesting failure is an index that can go off the end.
        for (var i = 0; i < 20; i++)
            Assert.That(SiteLooks.All, Does.Contain(SiteLooks.Choose()));
    }

    /// <summary>
    /// Not a rule about taste — a rule about whether text on the brand colour can be read. Every look's brand
    /// is used as a button background with white text, so its lightness has to stay in the band the template
    /// was designed against.
    /// </summary>
    [Test]
    public void No_look_is_so_pale_that_white_text_would_disappear_on_it()
    {
        Assert.Multiple(() =>
        {
            foreach (var look in SiteLooks.All)
            {
                Assert.That(look.Chroma, Is.InRange(0.0, 0.25), look.Name);
                Assert.That(look.Hue, Is.InRange(0, 360), look.Name);
                Assert.That(look.Radius, Does.Match(@"^[\d.]+(rem|px)$"), look.Name);
            }
        });
    }
}
