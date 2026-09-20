using Webly.Data;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Renames a site. <b>The slug does not follow the name</b>, deliberately: the subdomain may already be published,
/// linked to and indexed, and silently moving somebody's live address because they fixed a typo in a label is a
/// broken link they did not ask for.
/// </summary>
public class RenameSite(ISiteRepository siteRepository, WeblyDbContext dbContext)
{
    public async Task<Result<SiteError>> ExecuteAsync(
        int userId,
        string nanoid,
        RenameSiteRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();

        if (name.Length is 0 or > 80) return Result<SiteError>.Fail(SiteError.InvalidName);

        var site = await siteRepository.FindForOwnerLightAsync(nanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError>.Fail(SiteError.NotFound);

        site.Name = name;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<SiteError>.Ok();
    }
}
