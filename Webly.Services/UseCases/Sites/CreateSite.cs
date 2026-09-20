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
/// the head pointer is written, so there is no state in which a site exists with nothing to check out — and no
/// code anywhere else that has to handle one.
///
/// The repository is created before the row is saved with its pointer, which is the safe order: a repository
/// with no row is invisible and costs a directory, while a row pointing at a repository that was never created
/// is a site that cannot be opened.
/// </summary>
public class CreateSite(
    ISiteRepository siteRepository,
    ISiteVersionRepository versionRepository,
    IUserRepository userRepository,
    ISiteRepositoryStore repositories,
    ISiteTemplateSource templates,
    SiteMapper mapper,
    IOptions<SitesOptions> sites,
    WeblyDbContext dbContext)
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
            Slug = await ReserveSlugAsync(name, cancellationToken)
        };

        siteRepository.Add(site);

        // Saved first, for the nanoid: it is the repository's name on disk, and the context mints it on insert.
        await dbContext.SaveChangesAsync(cancellationToken);

        var commit = await repositories.InitializeAsync(
            site.Nanoid,
            site.DefaultBranch,
            await templates.ReadAsync(cancellationToken),
            new CommitAuthor(user.DisplayName, user.Email),
            "Created from the Webly starter template",
            cancellationToken);

        var version = new SiteVersion
        {
            SiteId = site.Id,
            CommitSha = commit.Sha,
            Summary = "Created from the Webly starter template",
            Origin = SiteVersionOrigin.Template,
            ChangedFileCount = commit.ChangedFileCount,
            CreatedByUserId = userId
        };

        versionRepository.Add(version);
        await dbContext.SaveChangesAsync(cancellationToken);

        site.HeadVersionId = version.Id;

        // The new site becomes the one the editor opens. For a person's first, this is what flips
        // MeResponse.HasSite and gets them out of onboarding.
        user.CurrentSiteId = site.Id;

        await dbContext.SaveChangesAsync(cancellationToken);

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
