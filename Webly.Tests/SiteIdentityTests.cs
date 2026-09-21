using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sites;

namespace Webly.Tests;

/// <summary>
/// The business's name, written into the starter template at creation.
///
/// What matters here is not that the header says the right thing — it is that the file still compiles when
/// somebody's business is called "Joe's Garage". This runs on every site anybody creates, on a string a
/// stranger typed, and the file it edits is the one every component imports: a broken literal is a site that
/// does not build, in a sandbox, with the failure arriving as a chat message nobody can act on.
/// </summary>
public class SiteIdentityTests
{
    private const string SiteTs =
        """
        /** What this business is called. */
        export const siteName = 'Your site';

        export const siteUrl = process.env.NEXT_PUBLIC_SITE_URL || undefined;
        """;

    private const string Brand =
        """
        ## Facts

        - **Name:** (not set yet)
        - **What it does:** (not set yet)
        """;

    private static WorkspaceTree Template() =>
        new([
            WorkspaceFile.Text(SiteIdentity.SourcePath, SiteTs),
            WorkspaceFile.Text(SiteIdentity.BrandPath, Brand),
        ]);

    private static string Source(WorkspaceTree tree) => tree.Find(SiteIdentity.SourcePath)!.AsText();

    [Test]
    public void The_name_reaches_the_source_and_the_confirmed_facts()
    {
        var applied = SiteIdentity.Applied(Template(), "Ridgeway Cycles");

        Assert.Multiple(() =>
        {
            Assert.That(Source(applied), Does.Contain("export const siteName = 'Ridgeway Cycles';"));
            Assert.That(applied.Find(SiteIdentity.BrandPath)!.AsText(), Does.Contain("- **Name:** Ridgeway Cycles"));

            // And nothing else in either file moved: the rest of `site.ts` is what the whole project imports.
            Assert.That(Source(applied), Does.Contain("NEXT_PUBLIC_SITE_URL"));
            Assert.That(applied.Find(SiteIdentity.BrandPath)!.AsText(), Does.Contain("- **What it does:** (not set yet)"));
        });
    }

    /// <summary>
    /// The apostrophe is not an exotic case — it is a normal English business name, and it ends the literal.
    /// </summary>
    [TestCase("Joe's Garage", @"Joe\'s Garage")]
    [TestCase(@"Back\Slash Ltd", @"Back\\Slash Ltd")]
    [TestCase("Quote's \\ both", @"Quote\'s \\ both")]
    public void A_name_that_would_end_the_literal_is_escaped(string name, string expected)
    {
        Assert.That(Source(SiteIdentity.Applied(Template(), name)), Does.Contain($"export const siteName = '{expected}';"));
    }

    [Test]
    public void A_name_carrying_a_line_break_stays_on_one_line()
    {
        // Two files, two reasons: in the source a literal cannot span lines at all, and in the markdown the
        // second line would read as a fact of its own that nobody stated.
        var applied = SiteIdentity.Applied(Template(), "Two\nLines\u0000Ltd");

        Assert.Multiple(() =>
        {
            Assert.That(Source(applied), Does.Contain("export const siteName = 'Two Lines Ltd';"));
            Assert.That(applied.Find(SiteIdentity.BrandPath)!.AsText(), Does.Contain("- **Name:** Two Lines Ltd"));
            Assert.That(applied.Find(SiteIdentity.BrandPath)!.AsText(), Does.Contain("- **What it does:** (not set yet)"));
        });
    }

    [Test]
    public void A_template_that_has_moved_either_file_is_left_exactly_as_it_is()
    {
        var elsewhere = new WorkspaceTree([WorkspaceFile.Text("src/app/page.tsx", "export default () => null;\n")]);

        Assert.That(SiteIdentity.Applied(elsewhere, "Anything"), Is.SameAs(elsewhere));

        var reshaped = new WorkspaceTree([WorkspaceFile.Text(SiteIdentity.SourcePath, "export const name = 1;\n")]);

        Assert.That(SiteIdentity.Applied(reshaped, "Anything"), Is.SameAs(reshaped));
    }
}
