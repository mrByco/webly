using System.Text.RegularExpressions;

namespace Webly.Services.Services.Email.Templates;

/// <summary>
/// A posted form field's name, as the owner should read it — the rule the editor's <c>models/field-label.ts</c>
/// applies on the Messages screen, here for the email.
///
/// That rule's own comment says how the defect was found: "by sending the form on a published site and reading the
/// inbox, which showed <c>name</c>, <c>email</c>, <c>phone</c>, <c>message</c> in lower case". The fix then went
/// to the screen and not to the inbox, so the notification — the copy an owner reads first, on a phone, and replies
/// from — went on showing the raw names. Two copies of one rule, in two languages, because the email is rendered
/// here and the screen there; <c>FieldLabelTests</c> asserts the same cases as the client's spec, so the two
/// cannot drift without one of them going red.
///
/// Deliberately only as far as a name can be tidied: separators become spaces, a camelCase hump becomes a break,
/// and the first letter is capitalised. It cannot know that <c>qty</c> means quantity, and a confident wrong word
/// above somebody's enquiry is worse than a plain one. A name already written as prose is left exactly as it is.
/// </summary>
internal static partial class FieldLabel
{
    public static string Of(string name)
    {
        var trimmed = name.Trim();

        // Already prose: the form named its fields the way it labelled them. `How can we help?` is not an identifier.
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace)) return trimmed;

        var words = Hump().Replace(Separators().Replace(trimmed, " "), "$1 $2")
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0) return trimmed;

        var sentence = string.Join(' ', words);

        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    [GeneratedRegex("[-_.]+")]
    private static partial Regex Separators();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex Hump();
}
