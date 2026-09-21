using Microsoft.Extensions.Logging;
using Webly.Data.Models.Sites;
using Webly.Services.Services.Repositories;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Keeps the handful of files Webly owns current in a site that was created before they changed.
///
/// A site is self-contained — its rules and its build contract are files in its own repository, which is what
/// makes it a real project somebody can clone and keep. The cost of that is drift: a site created last month
/// has last month's copies of both.
///
/// <b><c>AGENTS.md</c> is the closest thing this product has to a prompt.</b> A site whose copy is old does
/// not know that a form must use <c>ContactForm</c> because the site cannot receive a post any other way, or
/// that photographs live in <c>public/images</c>. That is not cosmetic drift; it is the agent confidently
/// doing the thing the rule was written to prevent.
///
/// <b><c>next.config.ts</c> is the build contract</b>, and the template's <c>AGENTS.md</c> is what makes
/// rewriting it safe: it is on the list of files the agent must not touch, so the only copy that can exist is
/// ours. A fix there — the static export's settings, or turning off the dev badge that Next drew on top of
/// every customer's preview of their own site — would otherwise reach new sites only, for ever.
///
/// So they are refreshed before a turn, and <b>as their own version</b>. The obvious cheaper thing
/// — letting the update ride along in the turn's own commit — is the mistake this codebase has already made
/// once: <c>npm install</c> rewrote <c>package-lock.json</c> during seeding and a turn's diff became the
/// headline somebody asked for plus eighty-four lines they did not. A person's version says what they asked
/// for; this one says what it is.
///
/// It is deliberately not a background job over every site. A site nobody is editing does not need current
/// files, and a migration that touched every repository at once would be a write to thousands of histories
/// for files no visitor ever sees.
/// </summary>
public class SyncWeblyOwnedFiles(
    ISiteTemplateSource templates,
    ISiteRepositoryStore repositories,
    CommitSiteVersion commitSiteVersion,
    ILogger<SyncWeblyOwnedFiles> logger)
{
    /// <summary>
    /// The agent's standing rules, and the one-line file that points at them.
    ///
    /// <c>CLAUDE.md</c> exists so that both CLIs read the same file — which means it is also the file that
    /// would silently stop pointing anywhere if the rules were ever renamed. Listed beside them so the pair
    /// moves together.
    /// </summary>
    public static readonly string[] Instructions = ["AGENTS.md", "CLAUDE.md"];

    /// <summary>
    /// The build contract: what Webly's publish and the editor's preview both depend on. Safe to rewrite
    /// because the template's own rules put it on the list the agent must not touch.
    /// </summary>
    public static readonly string[] BuildContract = ["next.config.ts"];

    /// <summary>Everything above, which is the whole of what Webly owns inside a site.</summary>
    public static readonly string[] Paths = [.. Instructions, .. BuildContract];

    /// <summary>
    /// Commits the current instructions if this site's differ. Returns the version it wrote, or null — the
    /// usual answer — when the site already has them.
    /// </summary>
    public async Task<SiteVersion?> ExecuteAsync(
        Site site,
        CommitAuthor author,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var head = site.HeadVersion;

        if (head is null) return null;

        var template = await templates.ReadAsync(cancellationToken);
        var current = Paths
            .Select(path => template.Find(path))
            .OfType<WorkspaceFile>()
            .ToList();

        if (current.Count == 0)
        {
            // The template has no instructions at all, which is a deployment problem rather than this site's.
            // Said once per turn is noisy; said never is a product whose agent has no rules and no sign of it.
            logger.LogWarning("The site template carries none of {Paths}.", string.Join(", ", Paths));

            return null;
        }

        // Two small reads before the expensive one. The usual answer is "they are the same", and the whole
        // tree of a site with photographs in it is megabytes — read per turn for a comparison that almost
        // always says no.
        var stale = new List<WorkspaceFile>();

        foreach (var file in current)
        {
            var existing = await repositories.ReadFileAsync(site.Nanoid, head.CommitSha, file.Path, cancellationToken);

            if (existing is null || !existing.Content.AsSpan().SequenceEqual(file.Content))
                stale.Add(file);
        }

        if (stale.Count == 0) return null;

        var tree = await repositories.ReadTreeAsync(site.Nanoid, head.CommitSha, cancellationToken);
        var files = tree.Files.Where(x => !stale.Any(y => y.Path == x.Path)).Concat(stale).ToList();

        var version = await commitSiteVersion.ExecuteAsync(
            site,
            new WorkspaceTree(files),
            author,
            userId,
            SiteVersionOrigin.Template,
            SummaryFor(stale),
            details: "Files Webly keeps up to date in every site: "
                + string.Join(", ", stale.Select(x => x.Path)) + ".",
            cancellationToken: cancellationToken);

        if (version is not null)
            logger.LogInformation(
                "Refreshed {Files} in {Site} as {Version}.",
                string.Join(", ", stale.Select(x => x.Path)),
                site.Nanoid,
                version.Nanoid);

        return version;
    }

    /// <summary>
    /// What the history entry says. Written for the person reading it — "Updated the editing instructions"
    /// next to "Set the headline to …" tells them something; the file names are in the details underneath.
    /// </summary>
    private static string SummaryFor(IEnumerable<WorkspaceFile> stale)
    {
        var paths = stale.Select(x => x.Path).ToList();
        var rules = paths.Any(Instructions.Contains);
        var build = paths.Any(BuildContract.Contains);

        return (rules, build) switch
        {
            (true, true) => "Updated the editing instructions and the build settings",
            (false, true) => "Updated this site's build settings",
            _ => "Updated the editing instructions"
        };
    }
}
