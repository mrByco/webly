using System.Text;
using Webly.Services.Services.Repositories;

namespace Webly.Services.Services.Sites;

/// <summary>
/// One file of the starter template, rewritten on the way to a site's first commit.
///
/// Two things do this — <see cref="SiteLooks"/> writes the colour and shape, <see cref="SiteIdentity"/> writes
/// the name — and they share one rule that is the whole reason this is a class rather than a line in each:
/// <b>a file that is not there, or that no longer has the shape being looked for, is left exactly as it is.</b>
/// Every site anybody ever creates goes through here, before anything has looked at the result. A site that
/// still says "Your site" in the default colour is a small disappointment; a site whose `site.ts` or
/// stylesheet we half-rewrote is one that does not build.
/// </summary>
public static class TemplateFile
{
    public static WorkspaceTree Rewritten(WorkspaceTree tree, string path, Func<string, string> rewrite)
    {
        var file = tree.Find(path);

        if (file is null) return tree;

        var original = Encoding.UTF8.GetString(file.Content);
        var rewritten = rewrite(original);

        // The same tree, not a copy of it: a caller comparing references is asserting that nothing happened.
        if (rewritten == original) return tree;

        return new WorkspaceTree([.. tree.Files.Select(x => x.Path == path ? WorkspaceFile.Text(path, rewritten) : x)]);
    }
}
