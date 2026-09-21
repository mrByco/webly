using Webly.Services.Services.Sites;

namespace Webly.Tests;

/// <summary>
/// OKLCH to hex, against the numbers a real browser produces.
///
/// This conversion exists for one file — the share card, drawn by a renderer with no CSS engine — and its
/// whole job is to agree with the stylesheet beside it. So the assertion is not "the maths is the reference
/// maths" but "a browser given the same colour paints these bytes": the expected values were sampled out of
/// Chromium, by setting each look's <c>oklch(52% c h)</c> as a background and reading the pixel back.
///
/// If a look is ever added whose colour is outside sRGB, expect this to disagree by a unit or two: a browser
/// reduces chroma until the colour fits where this clamps each channel. That is written down in
/// <see cref="Oklch"/> and is a deliberate simplification, not an oversight — but a *new* look should be
/// checked against a browser the same way rather than assumed.
/// </summary>
public class OklchTests
{
    [TestCase(0.19, 268, "#3b5bd4")]
    [TestCase(0.15, 45, "#ab4400")]
    [TestCase(0.12, 150, "#287c42")]
    [TestCase(0.15, 220, "#0079a4")]
    [TestCase(0.04, 285, "#666680")]
    public void Each_look_is_the_colour_a_browser_paints(double chroma, int hue, string expected)
    {
        Assert.That(Oklch.ToHex(0.52, chroma, hue), Is.EqualTo(expected));
    }

    [Test]
    public void Black_and_white_are_where_they_should_be()
    {
        // The two ends, because an error in the transfer function is invisible in the middle of the range and
        // obvious here.
        Assert.Multiple(() =>
        {
            Assert.That(Oklch.ToHex(0, 0, 0), Is.EqualTo("#000000"));
            Assert.That(Oklch.ToHex(1, 0, 0), Is.EqualTo("#ffffff"));
        });
    }

    [Test]
    public void A_colour_outside_the_gamut_still_produces_a_colour()
    {
        // Not a claim about which colour — a claim that nothing throws and nothing overflows a byte. A hex
        // with a negative channel in it is a file that does not parse, in a build somebody is waiting on.
        var hex = Oklch.ToHex(0.52, 0.4, 130);

        Assert.That(hex, Does.Match("^#[0-9a-f]{6}$"));
    }
}
