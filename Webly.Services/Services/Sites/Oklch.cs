namespace Webly.Services.Services.Sites;

/// <summary>
/// An OKLCH colour as the hex a renderer without a browser in it can use.
///
/// <b>This exists because one thing in a site cannot read CSS.</b> The template's whole palette is derived from
/// an OKLCH hue and chroma, which is what makes "make it green" one edit — but the Open Graph card is drawn at
/// build time by Satori, which has no CSS engine and refuses <c>oklch(…)</c> outright ("Unexpected token type:
/// function"). So that one file carries a hex, and this is what produces it from the same two numbers, rather
/// than a sixth colour somebody would have to keep in step by eye.
///
/// The maths is the OKLab reference conversion — OKLab to cone responses, cubed, through the sRGB matrix, then
/// the sRGB transfer function. Channels outside the gamut are <b>clamped</b>, where a browser reduces chroma
/// until the colour fits. For the five looks the template ships they agree to the byte, which is what the test
/// asserts against values sampled out of a real browser; a look far outside sRGB would land a shade off, and a
/// share card a shade off is a smaller problem than a second definition of what the brand colour is.
/// </summary>
public static class Oklch
{
    /// <summary>The colour as <c>#rrggbb</c>.</summary>
    /// <param name="lightness">0 to 1, not a percentage.</param>
    /// <param name="chroma">0 to about 0.37 in sRGB.</param>
    /// <param name="hue">Degrees.</param>
    public static string ToHex(double lightness, double chroma, double hue)
    {
        var (red, green, blue) = ToRgb(lightness, chroma, hue);

        return $"#{red:x2}{green:x2}{blue:x2}";
    }

    private static (int Red, int Green, int Blue) ToRgb(double lightness, double chroma, double hue)
    {
        var radians = hue * Math.PI / 180.0;
        var a = chroma * Math.Cos(radians);
        var b = chroma * Math.Sin(radians);

        // OKLab to the cube roots of the cone responses, then the responses themselves.
        var longCone = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
        var mediumCone = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
        var shortCone = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);

        var red = 4.0767416621 * longCone - 3.3077115913 * mediumCone + 0.2309699292 * shortCone;
        var green = -1.2684380046 * longCone + 2.6097574011 * mediumCone - 0.3413193965 * shortCone;
        var blue = -0.0041960863 * longCone - 0.7034186147 * mediumCone + 1.7076147010 * shortCone;

        return (Channel(red), Channel(green), Channel(blue));
    }

    /// <summary>One linear channel as a byte, through the sRGB transfer function.</summary>
    private static int Channel(double linear)
    {
        var clamped = Math.Clamp(linear, 0.0, 1.0);

        var encoded = clamped > 0.0031308
            ? 1.055 * Math.Pow(clamped, 1.0 / 2.4) - 0.055
            : 12.92 * clamped;

        return (int)Math.Round(encoded * 255.0);
    }
}
