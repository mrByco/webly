using Microsoft.Extensions.Options;
using Webly.Data;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Sites;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Creates a site, its first version and its subdomain, in one go.
///
/// <b>A site never exists without a version.</b> The row and the starter document are written in the same
/// transaction, so there is no moment — and no failure mode — in which a site exists with nothing to render, and
/// therefore no code anywhere else that has to handle one.
/// </summary>
public class CreateSite(
    ISiteRepository siteRepository,
    ISiteVersionRepository versionRepository,
    IUserRepository userRepository,
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

        var site = new Site
        {
            OwnerId = userId,
            Name = name,
            Slug = await ReserveSlugAsync(name, cancellationToken)
        };

        siteRepository.Add(site);
        await dbContext.SaveChangesAsync(cancellationToken);

        var version = new SiteVersion
        {
            SiteId = site.Id,
            Document = StarterTemplate.For(name),
            Summary = "Created from the starter template",
            Origin = SiteVersionOrigin.Template,
            CreatedByUserId = userId
        };

        versionRepository.Add(version);
        await dbContext.SaveChangesAsync(cancellationToken);

        site.DraftVersionId = version.Id;

        // The new site becomes the one the editor opens. A person who just created a site is looking at it — and
        // for their first, this is what flips MeResponse.HasSite and gets them out of onboarding.
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        if (user is not null) user.CurrentSiteId = site.Id;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<SiteError, SiteSummaryResponse>.Ok(mapper.ToSummary(site, primaryDomain: null, publishedAt: null));
    }

    /// <summary>
    /// A free slug. The loop is bounded because the fallback is random: if five attempts in a row collide,
    /// something is wrong that retrying will not fix, and the unique index behind it is the real guarantee anyway.
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
