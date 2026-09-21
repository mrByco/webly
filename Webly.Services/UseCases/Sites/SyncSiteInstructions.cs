using Microsoft.Extensions.Logging;
using Webly.Data.Models.Sites;
using Webly.Services.Services.Repositories;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Keeps the agent's standing instructions current in a site that was created before they changed.
///
/// <b><c>AGENTS.md</c> is the closest thing this product has to a prompt</b>, and it lives in each site's own
/// repository — which is what makes a site self-contained and what makes the instructions go stale. A site
/// created last month has last month's rules: it would not know that a form must use <c>ContactForm</c>
/// because the site it is editing cannot receive a post any other way, or that photographs live in
/// <c>public/images</c>. That is not a cosmetic drift; it is the agent confidently doing the thing the rules
/// were written to prevent.
///
/// So the instructions are refreshed before a turn, and <b>as their own version</b>. The obvious cheaper thing
/// — letting the update ride along in the turn's own commit — is the mistake this codebase has already made
/// once: <c>npm install</c> rewrote <c>package-lock.json</c> during seeding and a turn's diff became the
/// headline somebody asked for plus eighty-four lines they did not. A person's version says what they asked
/// for; this one says what it is.
///
/// It is deliberately not a background job over every site. A site nobody is editing does not need current
/// instructions, and a migration that touched every repository at once would be a write to thousands of
/// histories for a file no visitor ever sees.
/// </summary>
public class SyncSiteInstructions(
    ISiteTemplateSource templates,
    ISiteRepositoryStore repositories,
    CommitSiteVersion commitSiteVersion,
    ILogger<SyncSiteInstructions> logger)
{
    /// <summary>
    /// What counts as the instructions: the rules, and the file that points at them.
    ///
    /// <c>CLAUDE.md</c> is one line long and exists so that both CLIs read the same file — which means it is
    /// also the file that would silently stop pointing anywhere if the rules were ever renamed. Listed here so
    /// that the pair moves together.
    /// </summary>
    public static readonly string[] Paths = ["AGENTS.md", "CLAUDE.md"];

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
            "Updated the editing instructions",
            details: "Webly's own guidance for the assistant that edits this site: "
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
}
