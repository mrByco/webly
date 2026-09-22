using System.Text.RegularExpressions;
using Webly.Services.Services.Repositories;

namespace Webly.Services.Services.Sites;

/// <summary>
/// The one fact Webly has about a business before anybody has said anything: what they called it.
///
/// <b>A brand-new site used to say "Your site".</b> Somebody typed "Ridgeway Cycles" on the screen that
/// creates a site, and the site they got — header, footer, browser tab, the title of every link they shared —
/// said "Your site" until an agent turn changed it. Their own name, already typed, already on the dashboard,
/// and the page did not know it. It is written into the source at creation for the same reason the look is:
/// the site's content is the site's source, so a name in a column somewhere else would be a second place it
/// lives and a second thing to keep in step.
///
/// It is written at creation and **whenever a rename asks for it** — <see cref="UseCases.Sites.RenameSite"/>
/// applies the same rewrite to the head commit. It used to be written once, on the argument that what is on
/// the page belongs to the site rather than to the dashboard's label for it; renaming a site and reading its
/// published home page is what ended that argument. The web address still never moves, and that is not the
/// same case: an address may already be printed on a van.
/// </summary>
public static partial class SiteIdentity
{
    /// <summary>Where the name lives in the source, as a literal every component reads.</summary>
    public const string SourcePath = "src/site.ts";

    /// <summary>The agent's memory of the business, whose first line is its name.</summary>
    public const string BrandPath = "content/brand.md";

    /// <summary>
    /// The template's tree with this name written into it: the source constant, and the first confirmed fact
    /// in the file the agent reads before it writes anything.
    ///
    /// Both are rewritten independently, and a file that has moved or been reshaped is left exactly as it is —
    /// a site that still says "Your site" is a disappointment, and a site whose `site.ts` we corrupted does not
    /// compile at all.
    /// </summary>
    public static WorkspaceTree Applied(WorkspaceTree template, string name)
    {
        var tree = TemplateFile.Rewritten(template, SourcePath, source =>
            Constant().Replace(source, $"export const siteName = '{ForTypeScript(name)}';"));

        return TemplateFile.Rewritten(tree, BrandPath, brand =>
            Fact().Replace(brand, $"- **Name:** {ForMarkdown(name)}"));
    }

    /// <summary>
    /// A name inside a single-quoted TypeScript string.
    ///
    /// <b>This is customer input reaching a source file that gets compiled</b>, in a sandbox, by a build whose
    /// failure is ours to explain. An apostrophe is an ordinary thing to have in a business's name — "Joe's
    /// Garage" — and unescaped it ends the literal and leaves a file that does not parse. The backslash has to
    /// go first, or escaping the quote would produce one that is itself escaped; the line breaks and control
    /// characters go because a literal cannot contain them and because a name is one line by definition.
    /// </summary>
    private static string ForTypeScript(string name) =>
        Control().Replace(name, " ").Replace("\\", "\\\\").Replace("'", "\\'");

    /// <summary>
    /// The same name in a markdown bullet. Nothing here is executed, so the only rule is that it stays on its
    /// own line: a newline would push the rest of the name into the list as a fact nobody stated.
    /// </summary>
    private static string ForMarkdown(string name) => Control().Replace(name, " ").Trim();

    /// <summary>
    /// The constant, <b>including a value this class has already escaped a quote into</b>.
    ///
    /// `'[^']*'` was the obvious pattern and it is wrong in one specific, common case: once the value is
    /// `'Joe\'s Garage'`, the character class stops at the escaped quote, the `';` that has to follow is not
    /// there, and nothing matches. So a site whose name has an apostrophe could be written **once** and never
    /// rewritten — the escaping that makes the first write safe is exactly what makes every later one miss.
    ///
    /// What that looked like: rename "Joe's Garage" to "Ridgeway Motors" with the checkbox ticked, get a 204, a
    /// version in the history called "Renamed the site to Ridgeway Motors", a dashboard saying Ridgeway Motors
    /// and `content/brand.md` saying it too — while every page, the header, the footer, each page's title and
    /// the share card went on saying Joe's Garage, for ever, because the next rename would miss in the same
    /// way. The commit's own diff touched `brand.md` and nothing else. Found by renaming a site called
    /// "Joe's Garage" in the running app and reading its source afterwards.
    ///
    /// It is the names most likely to have one — O'Brien, Joe's, Sainsbury's — so this was not an edge.
    /// </summary>
    [GeneratedRegex(@"export const siteName = '(?:[^'\\]|\\.)*';")]
    private static partial Regex Constant();

    [GeneratedRegex(@"- \*\*Name:\*\* .*")]
    private static partial Regex Fact();

    /// <summary>Anything that is not text a name can contain, line breaks included.</summary>
    [GeneratedRegex(@"[\p{Cc}\p{Cf}]")]
    private static partial Regex Control();
}
