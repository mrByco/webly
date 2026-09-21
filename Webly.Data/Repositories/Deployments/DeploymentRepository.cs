using Microsoft.EntityFrameworkCore;
using Webly.Data.Models.Deployments;

namespace Webly.Data.Repositories.Deployments;

public class DeploymentRepository(WeblyDbContext dbContext) : IDeploymentRepository
{
    /// <summary>The statuses a deployment can still move out of on its own.</summary>
    private static readonly DeploymentStatus[] InFlight =
        [DeploymentStatus.Queued, DeploymentStatus.Preparing, DeploymentStatus.Building];

    public Task<Deployment?> FindAsync(string nanoid, CancellationToken cancellationToken = default) =>
        dbContext.Deployments.FirstOrDefaultAsync(x => x.Nanoid == nanoid, cancellationToken);

    public Task<Deployment?> FindWithSiteAsync(string nanoid, CancellationToken cancellationToken = default) =>
        dbContext.Deployments
            .Include(x => x.Site)
            .Include(x => x.SiteVersion)
            .FirstOrDefaultAsync(x => x.Nanoid == nanoid, cancellationToken);

    public Task<List<Deployment>> ListForSiteAsync(int siteId, int take, CancellationToken cancellationToken = default) =>
        dbContext.Deployments
            .Where(x => x.SiteId == siteId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<Deployment?> FindLiveAsync(
        int siteId,
        int siteVersionId,
        CancellationToken cancellationToken = default) =>
        dbContext.Deployments
            .Where(x => x.SiteId == siteId
                && x.SiteVersionId == siteVersionId
                && x.Status == DeploymentStatus.Ready)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Deployment?> FindInFlightAsync(int siteId, CancellationToken cancellationToken = default) =>
        dbContext.Deployments
            .Where(x => x.SiteId == siteId && InFlight.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<Deployment>> ListQueuedAsync(int take, CancellationToken cancellationToken = default) =>
        dbContext.Deployments
            .Where(x => x.Status == DeploymentStatus.Queued)
            .OrderBy(x => x.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public void Add(Deployment deployment) => dbContext.Deployments.Add(deployment);
}
