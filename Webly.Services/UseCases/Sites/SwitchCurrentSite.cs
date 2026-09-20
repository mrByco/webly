using Webly.Data;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;

namespace Webly.Services.UseCases.Sites;

/// <summary>Changes which site the editor opens on. See <c>User.CurrentSiteId</c>.</summary>
public class SwitchCurrentSite(
    ISiteRepository siteRepository,
    IUserRepository userRepository,
    WeblyDbContext dbContext)
{
    public async Task<Result<SiteError>> ExecuteAsync(
        int userId,
        string nanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(nanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError>.Fail(SiteError.NotFound);

        var user = await userRepository.FindByIdAsync(userId, cancellationToken);

        if (user is null) return Result<SiteError>.Fail(SiteError.NotFound);

        user.CurrentSiteId = site.Id;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<SiteError>.Ok();
    }
}
