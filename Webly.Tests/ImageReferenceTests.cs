using System.Text;
using Webly.Services.Services.Repositories;
using Webly.Services.UseCases.Assets;

namespace Webly.Tests;

/// <summary>
/// Which files count as "a page still uses this photograph" — the rule that decides whether deleting one is
/// safe, where a false positive costs a sentence in the chat and a false negative costs a broken image on a
/// real business's website.
/// </summary>
public class ImageReferenceTests
{
    private static WorkspaceTree Tree(params (string Path, string Content)[] files) =>
        new([.. files.Select(x => WorkspaceFile.Text(x.Path, x.Content))]);

    [Test]
    public void A_page_that_shows_the_image_is_named()
    {
        var tree = Tree(
            ("src/app/page.tsx", "<img src=\"/images/shopfront.jpg\" alt=\"Our shop\" />"),
            ("src/app/globals.css", ".hero { background: url('/images/shopfront.jpg'); }"),
            ("src/app/contact/page.tsx", "nothing here"));

        Assert.That(
            ImageReferences.UsedBy(tree, "/images/shopfront.jpg"),
            Is.EqualTo(new[] { "src/app/globals.css", "src/app/page.tsx" }),
            "a src attribute and a CSS url(), because text is what finds both");
    }

    [Test]
    public void Prose_about_the_site_is_not_a_page_of_it()
    {
        // The bug this exists for, found the first time a delete was tried: AGENTS.md uses
        // /images/shopfront.jpg as its example, so every site refused to delete a photograph with that name.
        // A note in brand.md naming the shop front picture would have done the same.
        var tree = Tree(
            ("AGENTS.md", "refer to it by the path without public — /images/shopfront.jpg"),
            ("CLAUDE.md", "See AGENTS.md."),
            ("content/brand.md", "- **Photo:** /images/shopfront.jpg is the front of the shop"),
            ("README.md", "/images/shopfront.jpg"));

        Assert.That(ImageReferences.UsedBy(tree, "/images/shopfront.jpg"), Is.Empty);
    }

    [Test]
    public void The_image_itself_and_its_neighbours_are_not_references()
    {
        var tree = Tree(
            ("public/images/shopfront.jpg", "binary-ish bytes naming /images/shopfront.jpg"),
            ("public/robots.txt", "/images/shopfront.jpg"));

        Assert.That(ImageReferences.UsedBy(tree, "/images/shopfront.jpg"), Is.Empty,
            "public/ is served rather than rendered, so nothing in it points at anything");
    }

    [Test]
    public void A_large_binary_under_src_is_not_read_as_text()
    {
        // A font or an icon can live under src/. Reading a megabyte of it to look for a URL it cannot contain
        // would make deleting one photograph cost the whole tree.
        var tree = new WorkspaceTree(
        [
            new WorkspaceFile("src/fonts/inter.woff2", new byte[600 * 1024]),
            WorkspaceFile.Text("src/app/page.tsx", "no image here"),
        ]);

        Assert.That(ImageReferences.UsedBy(tree, "/images/shopfront.jpg"), Is.Empty);
    }

    [Test]
    public void A_different_image_with_a_similar_name_is_not_a_match()
    {
        var tree = Tree(("src/app/page.tsx", "<img src=\"/images/shopfront-2.jpg\" alt=\"\" />"));

        Assert.That(ImageReferences.UsedBy(tree, "/images/shopfront-2.jpg"), Is.Not.Empty);

        // And the other way round is the one that matters: deleting shopfront.jpg must not be blocked by a
        // page using shopfront-2.jpg. It is, because "/images/shopfront.jpg" is not a substring of it.
        Assert.That(ImageReferences.UsedBy(tree, "/images/shopfront.jpg"), Is.Empty);
    }
}
