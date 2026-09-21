using Microsoft.EntityFrameworkCore;
using Npgsql;
using Webly.Data;
using Webly.Data.Models.Deployments;
using Webly.Data.Repositories.Deployments;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Deployments;
using Webly.Services.Services.Deployments;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services.Realtime;
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
    RunRegistry runs,
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

            // Saved on its own, before the new row is inserted. A partial unique index says a site has at most
            // one publish in flight, and it is checked per statement — so this has to have happened before the
            // insert, and the order of two changes inside one SaveChanges is EF's business rather than ours.
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var deployment = new Deployment
        {
            SiteId = site.Id,
            SiteVersionId = headVersionId,
            Status = DeploymentStatus.Queued,
            TriggeredByUserId = userId
        };

        deployments.Add(deployment);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsOneInFlightViolation(exception))
        {
            // Two publishes a millisecond apart — a double-click, or two tabs. Both read no deploy in flight,
            // both insert, and the index refuses the loser. Without it both ran: two sandboxes, two `npm ci`s,
            // two real builds and two "your site is live" emails for one press of one button, which is the
            // product's most expensive operation doubled for nothing.
            //
            // The other request is already publishing exactly this, so the honest answer is its deployment: the
            // editor then watches the run that is really going to happen.
            dbContext.Entry(deployment).State = EntityState.Detached;

            var existing = await deployments.FindInFlightAsync(site.Id, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Site {site.Nanoid} has no deployment in flight, and inserting one was refused.", exception);

            return Result<DeployError, DeploymentResponse>.Ok(
                DeploymentMapper.ToResponse(existing, site.HeadVersion));
        }

        // The run exists from here, not from when the runner picks the row up.
        //
        // The client's two steps are start, then watch — the same pair as an agent turn, so that a reload
        // re-attaches by the same path. But a deploy's work starts on the runner's next poll, up to three seconds
        // later, and until it did there was no run to watch: `Subscribe` answered "that run could not be found" and
        // the editor showed an error alert over a publish that was going perfectly well. **Every publish through the
        // UI looked like a failure.** Registering here closes the gap; the runner asks for the same id and gets this
        // handle back.
        runs.Register(RunKind.Deploy, deployment.Nanoid, deployment.Nanoid, userId);

        return Result<DeployError, DeploymentResponse>.Ok(
            DeploymentMapper.ToResponse(deployment, site.HeadVersion));
    }

    /// <summary>
    /// Whether this is the index that says one site publishes one thing at a time. Matched by name rather than
    /// by <c>23505</c> alone, so that a different unique violation still fails loudly.
    /// </summary>
    private static bool IsOneInFlightViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: "23505" } postgres
        && postgres.ConstraintName == "IX_Deployments_OneInFlightPerSite";
}
