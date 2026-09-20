using Webly.Data.Models.Deployments;

namespace Webly.Data.Repositories.Deployments;

public interface IDeploymentRepository
{
    Task<Deployment?> FindAsync(string nanoid, CancellationToken cancellationToken = default);

    /// <summary>With its site and version loaded, for the runner — which has a nanoid and nothing else.</summary>
    Task<Deployment?> FindWithSiteAsync(string nanoid, CancellationToken cancellationToken = default);

    Task<List<Deployment>> ListForSiteAsync(int siteId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// A deploy of this site that has not finished yet, if there is one. Publishing while one is in
    /// flight cancels it rather than queueing a second — the newer document is what the person wants
    /// live, and two concurrent uploads to one provider project race to decide which.
    /// </summary>
    Task<Deployment?> FindInFlightAsync(int siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queued rows, oldest first, for the job runner to lease. A single-instance runner, so no lease
    /// column yet — <c>DeploymentJobRunner</c> documents what a second instance would need.
    /// </summary>
    Task<List<Deployment>> ListQueuedAsync(int take, CancellationToken cancellationToken = default);

    void Add(Deployment deployment);
}
