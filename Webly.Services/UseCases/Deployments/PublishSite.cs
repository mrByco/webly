using Webly.Data;
using Webly.Data.Models.Deployments;
using Webly.Data.Repositories.Deployments;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Deployments;
using Webly.Services.Services.Deployments;
using Webly.Services.UseCases.Deployments.Mapping;

namespace Webly.Services.UseCases.Deployments;

/// <summary>
/// Queues a deployment of the site's current draft. Returns as soon as the row exists — the work happens in
/// <c>DeploymentJobRunner</c>.
///
/// <b>Queued, not done.</b> A publish takes tens of seconds and reaches an outside service, so the endpoint cannot
/// be the thing that waits: a client that loses its connection must still be able to find out what happened, and the
/// row is what makes that possible. That is the whole difference from an agent turn, which lives in memory and dies
/// with the process because nothing outside has changed yet.
///
/// An in-flight publish of the same site is cancelled rather than queued behind: the newer draft is what the person
/// wants live, and two uploads into one provider project race to decide which of them wins.
/// </summary>
public class PublishSite(
    ISiteRepository siteRepository,
    IDeploymentRepository deployments,
    IDeploymentTarget deploymentTarget,
    WeblyDbContext dbContext)
{
    public async Task<Result<DeployError, DeploymentResponse>> ExecuteAsync(
        int userId,
        string siteNanoid,
        PublishSiteRequest request,
        CancellationToken cancellationToken = default)
    {
        // Asked of the target, not of a configuration key: which credential publishing needs is the target's
        // business, and the development target needs none.
        if (!deploymentTarget.IsConfigured)
            return Result<DeployError, DeploymentResponse>.Fail(DeployError.PublishingUnavailable);

        var site = await siteRepository.FindForOwnerAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<DeployError, DeploymentResponse>.Fail(DeployError.SiteNotFound);

        if (site.HeadVersionId is not { } headVersionId || site.HeadVersion is null)
            return Result<DeployError, DeploymentResponse>.Fail(DeployError.NothingToPublish);

        // Unless the person asked for exactly this. See PublishSiteRequest.Republish.
        if (site.PublishedVersionId == headVersionId && !request.Republish)
            return Result<DeployError, DeploymentResponse>.Fail(DeployError.NothingToPublish);

        if (await deployments.FindInFlightAsync(site.Id, cancellationToken) is { } inFlight)
        {
            inFlight.Status = DeploymentStatus.Cancelled;
            inFlight.FinishedAt = DateTime.UtcNow;
            inFlight.Error = "Superseded by a newer publish.";
        }

        var deployment = new Deployment
        {
            SiteId = site.Id,
            SiteVersionId = headVersionId,
            Status = DeploymentStatus.Queued,
            TriggeredByUserId = userId
        };

        deployments.Add(deployment);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<DeployError, DeploymentResponse>.Ok(
            DeploymentMapper.ToResponse(deployment, site.HeadVersion));
    }
}
