using Microsoft.Extensions.Logging;
using Webly.Data;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Users;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sites;
using Webly.Services.Services.Workspaces;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Renames a site — the label on the dashboard, and, if the person asks for it, the name on the pages.
///
/// <b>The slug does not follow the name</b>, deliberately: the subdomain may already be published, linked to
/// and indexed, and silently moving somebody's live address because they fixed a typo in a label is a broken
/// link they did not ask for.
///
/// <b>The pages, though, had the opposite problem.</b> Renaming changed one row, so a person who renamed
/// "My Shop" to "Ridgeway Cycles" had a dashboard that said one thing and a website — header, footer, every
/// page's title, the card a shared link shows — that said the other, with nothing anywhere saying so. The
/// screen's own line, "the name is yours to change; the web address is not", made it worse by answering the
/// question next to the one being asked. Found by renaming a site and reading its published home page.
///
/// So the rename can write the name into the site as well, as its own version: <see cref="SiteIdentity"/>'s
/// rewrite — the same one that puts the name there when the site is created — applied to the head commit and
/// committed through <see cref="CommitSiteVersion"/>, like everything else that changes a site. Safe by
/// construction rather than by care: <see cref="TemplateFile"/> leaves a file that has moved or been reshaped
/// exactly as it is, and a tree identical to its parent commits nothing at all.
/// </summary>
public class RenameSite(
    ISiteRepository siteRepository,
    IUserRepository users,
    ISiteRepositoryStore repositories,
    ISiteWorkspaceRegistry workspaces,
    CommitSiteVersion commitSiteVersion,
    WeblyDbContext dbContext,
    ILogger<RenameSite> logger)
{
    public async Task<Result<SiteError>> ExecuteAsync(
        int userId,
        string nanoid,
        RenameSiteRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();

        if (name.Length is 0 or > 80) return Result<SiteError>.Fail(SiteError.InvalidName);

        // The full lookup rather than the light one, because the commit below needs the head version. A rename
        // is a button somebody presses once, so the extra rows are not a cost worth avoiding.
        var site = await siteRepository.FindForOwnerAsync(nanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError>.Fail(SiteError.NotFound);

        site.Name = name;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (request.ApplyToSite) await ApplyToSiteAsync(site, name, userId, cancellationToken);

        return Result<SiteError>.Ok();
    }

    /// <summary>
    /// Writes the new name into the site's source as a version of its own.
    ///
    /// Two small reads before the expensive one, the same shape <see cref="SyncWeblyOwnedFiles"/> uses: the
    /// whole tree of a site with photographs in it is megabytes, and the two files this touches are a few
    /// hundred bytes each. If neither changes — the agent reworded them, or the name is already there — the
    /// tree is never read and nothing is committed.
    /// </summary>
    private async Task ApplyToSiteAsync(Site site, string name, int userId, CancellationToken cancellationToken)
    {
        var head = site.HeadVersion;

        if (head is null) return;

        var paths = new[] { SiteIdentity.SourcePath, SiteIdentity.BrandPath };
        var before = new List<WorkspaceFile>();

        foreach (var path in paths)
        {
            var file = await repositories.ReadFileAsync(site.Nanoid, head.CommitSha, path, cancellationToken);

            if (file is not null) before.Add(file);
        }

        var after = SiteIdentity.Applied(new WorkspaceTree(before), name);
        var changed = after.Files
            .Where(file => before.First(x => x.Path == file.Path).Content.AsSpan().SequenceEqual(file.Content) is false)
            .ToList();

        if (changed.Count == 0) return;

        var tree = await repositories.ReadTreeAsync(site.Nanoid, head.CommitSha, cancellationToken);
        var files = tree.Files.Where(x => changed.All(y => y.Path != x.Path)).Concat(changed).ToList();

        var user = await users.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} disappeared mid-rename.");

        var version = await commitSiteVersion.ExecuteAsync(
            site,
            new WorkspaceTree(files),
            new CommitAuthor(user.DisplayName, user.Email),
            userId,
            SiteVersionOrigin.Manual,
            $"Renamed the site to \"{name}\"",
            details: $"Changed {string.Join(" and ", changed.Select(x => x.Path))}, which is where the site's "
                + "header, footer and page titles read its name from.",
            cancellationToken: cancellationToken);

        if (version is null) return;

        // The same reason a restore and an upload re-seed rather than releasing: somebody is looking at the
        // preview of the site they have just renamed, and the point of ticking the box was to see it say so.
        await workspaces.ReseedAsync(site, version.CommitSha, cancellationToken);

        logger.LogInformation("Wrote the new name into {Site} as {Version}.", site.Nanoid, version.Nanoid);
    }
}
