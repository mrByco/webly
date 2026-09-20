using Webly.Data.Repositories.Deployments;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Deployments;
using Webly.Services.UseCases.Deployments.Mapping;

namespace Webly.Services.UseCases.Deployments;

/// <summary>
/// A site's recent publishes, newest first — the failed ones included. "It failed twice with the same error at 11pm"
/// is the only evidence anybody has when a provider misbehaves.
/// </summary>
public class ListDeployments(
    ISiteRepository siteRepository,
    IDeploymentRepository deployments,
    ISiteVersionRepository versions)
{
    public async Task<Result<DeployError, IReadOnlyList<DeploymentResponse>>> ExecuteAsync(
        int userId,
        string siteNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<DeployError, IReadOnlyList<DeploymentResponse>>.Fail(DeployError.SiteNotFound);

        var rows = await deployments.ListForSiteAsync(site.Id, take: 20, cancellationToken);
        var responses = new List<DeploymentResponse>(rows.Count);

        foreach (var deployment in rows)
        {
            var version = await versions.FindByIdForSiteAsync(deployment.SiteVersionId, site.Id, cancellationToken);

            if (version is not null) responses.Add(DeploymentMapper.ToResponse(deployment, version));
        }

        return Result<DeployError, IReadOnlyList<DeploymentResponse>>.Ok(responses);
    }
}
