using System.Globalization;

namespace Webly.Services.Services.Domains;

/// <summary>
/// Normalizing what somebody typed into a hostname. People paste <c>https://www.example.com/</c>, type a trailing
/// dot, or use an internationalized name — all of which are the same domain to a registrar and three different
/// strings to a unique index, so this runs before anything is stored or sent to the provider.
/// </summary>
public static class Hostname
{
    /// <summary>The normalized hostname, or null when the input is not one at all.</summary>
    public static string? TryNormalize(string input)
    {
        var text = input.Trim().ToLowerInvariant().TrimEnd('.');

        // A pasted URL is the common case, not an error worth refusing.
        if (text.StartsWith("http://", StringComparison.Ordinal) || text.StartsWith("https://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;

            text = uri.Host;
        }

        text = text.Split('/')[0];

        if (text.Length is 0 or > 253 || text.Contains(' ') || text.Contains('@') || !text.Contains('.'))
            return null;

        // Punycode, because that is what DNS holds and what the provider answers with. Comparing a unicode hostname
        // against a punycode one is a domain that looks connected twice and works zero times.
        try
        {
            text = new IdnMapping().GetAscii(text);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return text.Split('.').All(IsLabel) ? text : null;
    }

    private static bool IsLabel(string label) =>
        label.Length is > 0 and <= 63
        && label[0] != '-'
        && label[^1] != '-'
        && label.All(x => char.IsAsciiLetterOrDigit(x) || x == '-');
}
