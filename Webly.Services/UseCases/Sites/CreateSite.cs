using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Webly.Data;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sites;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Creates a site: a row, a git repository holding the starter project, and the subdomain it will publish at.
///
/// <b>A site never exists without a commit.</b> The repository is initialized and its first commit made before
/// anything is written to the database, so there is no state in which a site exists with nothing to check out —
/// and no code anywhere else that has to handle one.
///
/// That order is the whole point and it is worth stating why, because the obvious order is the wrong one.
/// Saving the row first is tempting — the context mints the nanoid on insert, and the nanoid is the
/// repository's name on disk — but then a repository step that fails (a missing template, a full disk) leaves a
/// site row with no head version: invisible to nothing, unopenable, counting against the owner's site limit and
/// holding their slug. So the nanoid is generated here instead, which
/// <c>WeblyDbContext.StampEntities</c> explicitly allows, and the two failure directions become:
///
/// <list type="bullet">
/// <item>repository fails → nothing exists. The person retries.</item>
/// <item>database fails → a repository with no row, which nothing can reach, and which this deletes on the way
/// out anyway. It costs a directory in the worst case.</item>
/// </list>
/// </summary>
public class CreateSite(
    ISiteRepository siteRepository,
    ISiteVersionRepository versionRepository,
    IUserRepository userRepository,
    ISiteRepositoryStore repositories,
    ISiteTemplateSource templates,
    SiteMapper mapper,
    IOptions<SitesOptions> sites,
    WeblyDbContext dbContext,
    ILogger<CreateSite> logger)
{
    public async Task<Result<SiteError, SiteSummaryResponse>> ExecuteAsync(
        int userId,
        CreateSiteRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();

        if (name.Length is 0 or > 80)
            return Result<SiteError, SiteSummaryResponse>.Fail(SiteError.InvalidName);

        if (await siteRepository.CountForOwnerAsync(userId, cancellationToken) >= sites.Value.MaxSitesPerUser)
            return Result<SiteError, SiteSummaryResponse>.Fail(SiteError.LimitReached);

        var user = await userRepository.FindByIdAsync(userId, cancellationToken);

        if (user is null) return Result<SiteError, SiteSummaryResponse>.Fail(SiteError.NotFound);

        var site = new Site
        {
            OwnerId = userId,
            Name = name,
            Slug = await ReserveSlugAsync(name, cancellationToken),

            // Generated here rather than by the context on insert, because the repository has to exist before
            // the row does and the repository is named after it. See the class comment.
            Nanoid = NanoidDotNet.Nanoid.Generate()
        };

        const string summary = "Created from the Webly starter template";

        // One template, but not one look: every site would otherwise be the same indigo page until somebody
        // asked for something else, and a product whose customers' sites look alike is a template mill. Three
        // numbers in one stylesheet, chosen here and never stored — the site's source is where its colour
        // lives, which is what makes "make it green" an ordinary edit. See SiteLooks.
        var look = SiteLooks.Choose();
        var template = SiteLooks.Applied(await templates.ReadAsync(cancellationToken), look);

        logger.LogInformation("Creating {Site} with the {Look} look.", site.Nanoid, look.Name);

        var commit = await repositories.InitializeAsync(
            site.Nanoid,
            site.DefaultBranch,
            template,
            new CommitAuthor(user.DisplayName, user.Email),
            summary,
            cancellationToken);

        try
        {
            siteRepository.Add(site);

            var version = new SiteVersion
            {
                Site = site,
                CommitSha = commit.Sha,
                Summary = summary,
                Origin = SiteVersionOrigin.Template,
                ChangedFileCount = commit.ChangedFileCount,
                CreatedByUserId = userId
            };

            versionRepository.Add(version);

            // Two saves rather than one: the head pointer needs the version's key, and the version needs the
            // site's. The navigation property is what lets EF order these two inserts; the pointer is an update
            // after them, which is exactly why that foreign key is deferrable.
            await dbContext.SaveChangesAsync(cancellationToken);

            site.HeadVersionId = version.Id;

            // The new site becomes the one the editor opens. For a person's first, this is what flips
            // MeResponse.HasSite and gets them out of onboarding.
            user.CurrentSiteId = site.Id;

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The repository exists and nothing points at it. Nobody can reach it — a site is only ever found
            // through its row — so this is tidiness rather than correctness, and it must not mask the real
            // failure: the original exception is what the caller needs to see.
            try
            {
                await repositories.DeleteAsync(site.Nanoid, CancellationToken.None);
            }
            catch (RepositoryException)
            {
                // Then it stays on disk. A directory is a smaller problem than the exception being thrown.
            }

            throw;
        }

        return Result<SiteError, SiteSummaryResponse>.Ok(
            mapper.ToSummary(site, primaryDomain: null, publishedAt: null));
    }

    /// <summary>
    /// A free slug. Bounded, because the fallback is random: if five attempts in a row collide something is
    /// wrong that retrying will not fix, and the unique index behind it is the real guarantee anyway.
    /// </summary>
    private async Task<string> ReserveSlugAsync(string name, CancellationToken cancellationToken)
    {
        var slug = SiteSlug.From(name);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!await siteRepository.SlugExistsAsync(slug, cancellationToken)) return slug;

            slug = SiteSlug.Disambiguate(SiteSlug.From(name));
        }

        return SiteSlug.Disambiguate(slug);
    }
}
