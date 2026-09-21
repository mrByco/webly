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

    /// <summary>The share card, whose colour is a hex because the thing that draws it has no CSS engine.</summary>
    private const string CardTsx =
        """
        const brand = '#3b5bd4';

        export default function OpenGraphImage() {
          return new ImageResponse(
            <div style={{ background: brand, color: '#ffffff' }}>
              <div style={{ background: '#ffffff' }} />
              {siteName}
            </div>,
            size,
          );
        }
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

    /// <summary>
    /// The third copy of the colour, and the one written differently. A card that stayed indigo on a green
    /// site is the kind of wrong that only shows up in somebody else's chat window, days later.
    /// </summary>
    [Test]
    public void The_share_card_carries_the_same_colour_as_a_hex()
    {
        Assert.Multiple(() =>
        {
            foreach (var look in SiteLooks.All)
            {
                var applied = SiteLooks.Applied(Template((SiteLooks.CardPath, CardTsx)), look);
                var card = applied.Find(SiteLooks.CardPath)!.AsText();

                Assert.That(card, Does.Contain($"const brand = '{Oklch.ToHex(0.52, look.Chroma, look.Hue)}';"), look.Name);

                // And the white on the card stays white. It went brand-coloured on a brand background the
                // first time this matched by role rather than by name, which is an element still on the card
                // and impossible to see.
                Assert.That(card, Does.Contain("color: '#ffffff'"), look.Name);
                Assert.That(card, Does.Contain("<div style={{ background: '#ffffff' }} />"), look.Name);
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

    [Test]
    public void Every_look_is_a_colour_and_a_length_that_css_accepts()
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

    /// <summary>
    /// White text on the brand colour is readable, as the standard defines readable rather than as a range of
    /// chroma stands in for it.
    ///
    /// This used to assert that the chroma was under 0.25 and call that "not so pale that white text would
    /// disappear". It is a proxy, and it is the wrong one: lightness decides contrast and chroma barely
    /// touches it, so a sixth look at 70% lightness would have sailed through and shipped a button nobody can
    /// read on somebody's business website. Now that <see cref="Oklch"/> exists the real number is two dozen
    /// lines away, so the test asks the real question.
    ///
    /// 4.5:1 is WCAG AA for normal-sized text. The five that ship are between 4.9 and 5.9, so there is room —
    /// which is worth knowing when the sixth is chosen, because the margin is where it will be spent.
    /// </summary>
    [Test]
    public void White_text_on_every_look_meets_the_contrast_standard()
    {
        Assert.Multiple(() =>
        {
            foreach (var look in SiteLooks.All)
            {
                var brand = Oklch.ToHex(0.52, look.Chroma, look.Hue);
                var ratio = Contrast(brand, "#ffffff");

                Assert.That(ratio, Is.GreaterThanOrEqualTo(4.5),
                    $"{look.Name} ({brand}) gives white text {ratio:0.00}:1");
            }
        });
    }

    /// <summary>The WCAG contrast ratio between two <c>#rrggbb</c> colours: sRGB to relative luminance, then
    /// the standard's own formula. Here rather than in the product, because nothing in the app computes it at
    /// run time — it is a rule about five constants, checked where the constants are.</summary>
    private static double Contrast(string left, string right)
    {
        var a = Luminance(left);
        var b = Luminance(right);

        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            var value = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255.0;

            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }
}
