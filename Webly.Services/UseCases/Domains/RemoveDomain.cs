using Microsoft.Extensions.Logging;
using Webly.Data;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Domains;
using Webly.Services.Services.Deployments;

namespace Webly.Services.UseCases.Domains;

/// <summary>
/// Disconnects a hostname.
///
/// <b>Including the site's main address</b>, and that reverses an earlier decision. It used to be refused —
/// "removing it would leave the site canonicalizing to a hostname it no longer serves" — with an answer
/// telling the owner to make another domain the main one first. Which is impossible when it is their only
/// one, and it is somebody's only one on every site that has ever connected a domain: the button was hidden
/// on that row, the message behind it was unreachable advice, and a domain somebody connected could never be
/// disconnected again.
///
/// Nothing is left canonicalizing to nothing, because a site always has its Webly subdomain — that is what
/// the first line of the domains screen says, and <c>SiteMapper.UrlFor</c> falls back to it the moment there
/// is no verified primary. So this removes the row and the address goes back to what it was before anybody
/// typed a hostname. The client asks first, and says what the address becomes.
/// </summary>
public class RemoveDomain(
    ISiteRepository siteRepository,
    IDomainRepository domainRepository,
    IDeploymentTarget deploymentTarget,
    WeblyDbContext dbContext,
    ILogger<RemoveDomain> logger)
{
    public async Task<Result<DomainError>> ExecuteAsync(
        int userId,
        string siteNanoid,
        string domainNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<DomainError>.Fail(DomainError.SiteNotFound);

        var domain = await domainRepository.FindForSiteAsync(domainNanoid, site.Id, cancellationToken);

        if (domain is null) return Result<DomainError>.Fail(DomainError.DomainNotFound);

        if (site.ProviderProjectId is { } projectId)
        {
            try
            {
                await deploymentTarget.RemoveDomainAsync(projectId, domain.Hostname, cancellationToken);
            }
            catch (DeploymentFailedException exception)
            {
                // The row goes anyway. A hostname left attached at the provider serves a site the owner has
                // disconnected, which is worth a log and a manual cleanup — but refusing to remove it here would
                // leave them unable to attach it to another site either.
                logger.LogWarning(exception,
                    "Could not detach {Hostname} at the provider; removing it from site {Site} regardless.",
                    domain.Hostname, siteNanoid);
            }
        }

        domainRepository.Remove(domain);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<DomainError>.Ok();
    }
}
