using Webly.Services.Services.Repositories;

namespace Webly.Services.UseCases.Assets;

/// <summary>
/// Which of a site's files still point at one of its images.
///
/// Its own class, with its own test, because the rule is easy to state and was wrong on the first attempt in
/// a way that would have shipped: searching every file that is not an image finds <c>AGENTS.md</c>, which
/// uses <c>/images/shopfront.jpg</c> as its example — so every site would have refused to delete a photograph
/// with that name — and a note in <c>content/brand.md</c> saying which picture is the shop front would have
/// done the same. Prose about a site is not a page of it.
/// </summary>
public static class ImageReferences
{
    /// <summary>
    /// Where a page can be. Everything the build turns into HTML is under here, and nothing under it is
    /// prose: <c>public/</c> is served rather than rendered, and the markdown files at the root are
    /// instructions and notes.
    /// </summary>
    private const string Source = "src/";

    /// <summary>
    /// Anything larger is not something with a URL typed into it. A font or a favicon will not name an image,
    /// and reading megabytes to find out would make deleting one photograph cost the whole tree.
    /// </summary>
    private const int MaxTextBytes = 512 * 1024;

    /// <summary>
    /// The source files that mention <paramref name="url"/>, in path order.
    ///
    /// Text, not a parser. The URL appears in a <c>src</c>, in a CSS <c>url()</c>, in a metadata block and in
    /// a string somebody built a class name with — a parser for each of those is three ways to miss the
    /// fourth, and what this answers is "is it safe to delete", where a false positive costs a sentence in
    /// the chat and a false negative costs a broken page on a real business's website.
    /// </summary>
    public static IReadOnlyList<string> UsedBy(WorkspaceTree tree, string url) =>
    [
        .. tree.Files
            .Where(x => x.Path.StartsWith(Source, StringComparison.Ordinal))
            .Where(x => x.Content.Length <= MaxTextBytes && x.AsText().Contains(url, StringComparison.Ordinal))
            .Select(x => x.Path)
            .Order(StringComparer.Ordinal)
    ];
}
