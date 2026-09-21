using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace Webly.Services.Services.Repositories;

/// <summary>
/// The Next.js project every new site starts as.
///
/// One template, deliberately. A gallery of twenty starters is a browsing UI, a thumbnail pipeline and a
/// decision to make before anybody has typed a sentence — and the agent restructures the page on the first
/// turn anyway. The starter's job is to be a real, building, accessible project with the conventions written
/// down in its own <c>AGENTS.md</c>, so the first thing a person ever asks for is an <i>edit</i>, which is what
/// a coding agent is good at, rather than a creation from nothing, which is where it invents.
/// </summary>
public interface ISiteTemplateSource
{
    /// <summary>The template's files. Read once and cached: it is on disk beside the app and never changes at
    /// runtime.</summary>
    Task<WorkspaceTree> ReadAsync(CancellationToken cancellationToken = default);
}

public class TemplateOptions
{
    public const string SectionName = "Templates";

    /// <summary>
    /// Where the starter project lives. A path rather than an embedded resource so that it stays a normal
    /// project somebody can open, run and build — which is exactly what it has to be, since its whole job is to
    /// be the thing an agent works in.
    /// </summary>
    [Required]
    public string SitePath { get; set; } = "templates/next-site";
}

public class DirectorySiteTemplateSource(
    IOptions<TemplateOptions> options,
    ILogger<DirectorySiteTemplateSource> logger) : ISiteTemplateSource
{
    /// <summary>
    /// Never travels into a repository: dependencies and build output are reproducible, and committing them
    /// would put 370 MB in every customer's first commit. The lockfile beside them is committed, because that
    /// is what makes the install reproducible.
    /// </summary>
    private static readonly string[] Excluded =
    [
        ".git", "node_modules", ".next", "out", ".vercel", ".turbo", ".env", ".env.local", ".DS_Store",

        // The typecheck's incremental cache. The template points it into `.next`, and this entry is here for
        // the developer who has an older one sitting in the template's root from before that: without it, a
        // machine-readable dump of somebody's working copy would be in every new site's first commit.
        "tsconfig.tsbuildinfo",
    ];

    private WorkspaceTree? _cached;

    public async Task<WorkspaceTree> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null) return _cached;

        var root = Path.GetFullPath(options.Value.SitePath);

        if (!Directory.Exists(root))
            throw new RepositoryException($"The site template is missing from '{root}'.");

        var files = new List<WorkspaceFile>();

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            if (Excluded.Any(excluded =>
                relative == excluded || relative.StartsWith($"{excluded}/", StringComparison.Ordinal)))
                continue;

            files.Add(new WorkspaceFile(relative, await File.ReadAllBytesAsync(path, cancellationToken)));
        }

        logger.LogInformation("Loaded the site template: {Files} files from {Root}.", files.Count, root);

        return _cached = new WorkspaceTree(files);
    }
}
