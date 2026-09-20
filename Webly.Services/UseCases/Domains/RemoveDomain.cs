using Microsoft.Extensions.Logging;
using Webly.Data;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Domains;
using Webly.Services.Services.Deployments;

namespace Webly.Services.UseCases.Domains;

/// <summary>
/// Disconnects a hostname. Refused for the primary one: removing it would leave the site canonicalizing to a
/// hostname it no longer serves, so promoting another domain first is a deliberate step rather than something to
/// infer.
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

        if (domain.IsPrimary) return Result<DomainError>.Fail(DomainError.IsPrimary);

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
