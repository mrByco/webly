using Microsoft.Extensions.Logging;
using Webly.Data;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Workspaces;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Deletes a site, everything under it, and its hosting.
///
/// Two ordering rules, both learned from the reference project's household deletion:
///
/// The pointers on the site row are cleared <b>before</b> the row goes, because they reference versions the cascade
/// is about to delete, and a constraint checked per triggered action rather than per statement does not care that
/// both sides are on their way out. (The deferred constraints in the migration are the other half of this; clearing
/// the pointers first means the delete does not depend on them.)
///
/// And <c>User.CurrentSiteId</c> is read before anything is removed: the FK nulls it on cascade, so a check
/// afterwards can no longer tell "was looking at this site" from "was looking at nothing".
/// </summary>
public class DeleteSite(
    ISiteRepository siteRepository,
    IDomainRepository domainRepository,
    IUserRepository userRepository,
    IDeploymentTarget deploymentTarget,
    ISiteWorkspaceRegistry workspaces,
    ISiteRepositoryStore repositories,
    WeblyDbContext dbContext,
    ILogger<DeleteSite> logger)
{
    public async Task<Result<SiteError>> ExecuteAsync(
        int userId,
        string nanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(nanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError>.Fail(SiteError.NotFound);

        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        var wasCurrent = user?.CurrentSiteId == site.Id;

        var domains = await domainRepository.ListForSiteAsync(site.Id, cancellationToken);

        // The live workspace first: a sandbox editing a site that is being deleted is a turn that will fail
        // confusingly, and stopping it costs a second.
        await workspaces.ReleaseAsync(site.Nanoid);

        site.HeadVersionId = null;
        site.PublishedVersionId = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        siteRepository.Remove(site);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasCurrent && user is not null)
        {
            // Their oldest remaining site, or nothing — in which case the app offers to create one again, exactly
            // as it does for a new account.
            var remaining = await siteRepository.ListForOwnerAsync(userId, cancellationToken);
            user.CurrentSiteId = remaining.Count > 0 ? remaining[^1].Id : null;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // The source, then the hosting: both last, both best-effort. Deleting the rows is what makes the delete
        // true, and a provider outage — or a repository that is already gone — must not leave somebody unable to
        // delete their own site. An orphaned directory or project is visible and cheap; a site that will not
        // delete costs trust.
        try
        {
            await repositories.DeleteAsync(nanoid, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not delete the repository of site {Site}.", nanoid);
        }

        if (site.ProviderProjectId is { } projectId)
        {
            foreach (var domain in domains)
            {
                try
                {
                    await deploymentTarget.RemoveDomainAsync(projectId, domain.Hostname, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception,
                        "Could not detach {Hostname} from deleted site {Site}; it may need removing by hand.",
                        domain.Hostname, nanoid);
                }
            }
        }

        return Result<SiteError>.Ok();
    }
}
