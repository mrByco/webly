using System.Globalization;
using System.Text;
using NanoidDotNet;

namespace Webly.Services.Services.Sites;

/// <summary>
/// Turns a site's name into the subdomain it is reachable at. Every site has one from the moment it is
/// created — see <c>Site.Slug</c>.
/// </summary>
public static class SiteSlug
{
    /// <summary>
    /// Reserved first labels. A site at <c>www.webly.site</c> or <c>api.webly.site</c> would shadow the
    /// platform itself, and one at <c>mail</c> or <c>autodiscover</c> would shadow records the zone needs.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "app", "admin", "mail", "smtp", "imap", "ftp", "cdn", "assets", "static",
        "status", "docs", "blog", "help", "support", "billing", "login", "auth", "webly",
        "autodiscover", "autoconfig", "_domainkey", "dmarc", "test", "staging", "preview"
    };

    /// <summary>
    /// A DNS-safe label from a human name: accents folded, everything else that is not a letter, a digit
    /// or a hyphen dropped. Folding rather than rejecting, because "Kovács Bicikli" is a perfectly good
    /// site name and the person should not have to invent an ASCII one.
    ///
    /// Never returns a reserved or empty label: both fall back to a random one, because a site with no
    /// address cannot be published and this is not a decision worth interrupting a signup for.
    /// </summary>
    public static string From(string siteName)
    {
        var folded = new StringBuilder();

        foreach (var character in siteName.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsAsciiLetterOrDigit(character))
                folded.Append(char.ToLowerInvariant(character));
            else if (folded.Length > 0 && folded[^1] != '-')
                folded.Append('-');
        }

        var slug = folded.ToString().Trim('-');

        // 63 is the DNS label limit; 40 leaves room for the disambiguating suffix below and keeps the
        // address something a person can read out over the phone.
        if (slug.Length > 40) slug = slug[..40].Trim('-');

        return slug.Length == 0 || Reserved.Contains(slug) ? Random() : slug;
    }

    /// <summary>
    /// The next candidate when a slug is taken. A short random suffix rather than a counter: counting
    /// needs a query per attempt and tells the world how many sites are called "bakery".
    /// </summary>
    public static string Disambiguate(string slug) => $"{slug}-{Token(5)}";

    private static string Random() => $"site-{Token(8)}";

    /// <summary>
    /// Random characters that are legal in a DNS label. Its own alphabet rather than nanoid's default
    /// lower-cased: the default contains <c>_</c>, which no hostname may hold, and lower-casing a
    /// mixed-case token silently halves the entropy the size was chosen for.
    /// </summary>
    private static string Token(int size) => Nanoid.Generate("abcdefghijkmnpqrstuvwxyz23456789", size);
}
