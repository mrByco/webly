using Microsoft.Extensions.Logging;
using Webly.Data;
using Webly.Data.Models.Deployments;
using Webly.Data.Repositories.Chat;
using Webly.Data.Repositories.Deployments;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Realtime;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Realtime;
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
    IConversationRepository conversations,
    IDeploymentRepository deployments,
    IDeploymentTarget deploymentTarget,
    ISiteWorkspaceRegistry workspaces,
    ISiteRepositoryStore repositories,
    RunRegistry runs,
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

        // A turn on the site first: stopped, and waited for. Releasing the workspace alone did not end it — a turn
        // still waking the site had no workspace to release yet, so it carried on: started a sandbox, installed,
        // ran, and crashed writing its reply into the conversation this deletes, with the sandbox left running
        // for a site that was gone. Stopping it lets it end the way a Stop does, while everything it touches still
        // exists. Bounded, because a turn that will not stop must not keep somebody from deleting their site.
        await StopTurnAsync(site.Id, userId, cancellationToken);

        // And a publish, for the same reason and a worse outcome: one that finished after the delete wrote the
        // deleted site back onto the internet, because removing the published copy had already happened.
        await StopPublishAsync(site.Id, userId, cancellationToken);

        // Then the live workspace: a sandbox editing a site that is being deleted is a turn that will fail
        // confusingly, and stopping it costs a second.
        await workspaces.ReleaseAsync(site.Nanoid);

        site.HeadVersionId = null;
        site.PublishedVersionId = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        siteRepository.Remove(site);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasCurrent && user is not null)
        {
            // The one they touched most recently, or nothing — in which case the app offers to create one again,
            // exactly as it does for a new account. `ListForOwnerAsync` orders by `UpdatedAt` descending, so
            // that is the first of what is left; this took the *last*, which is the site its owner has cared
            // about least, and is what the editor would then open on.
            var remaining = await siteRepository.ListForOwnerAsync(userId, cancellationToken);
            user.CurrentSiteId = remaining.Count > 0 ? remaining[0].Id : null;

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

            // And then the hosting itself, which is what makes the word mean what it says. Detaching the
            // custom domains was all this did, so a deleted site went on answering at its Webly subdomain and
            // at the provider's own URL — the rows were gone and the page was not. Somebody deletes a site to
            // get it off the internet; anything less than this is the product not doing the one thing they
            // asked for. Found by deleting a published site and asking for it again.
            try
            {
                await deploymentTarget.DeleteProjectAsync(projectId, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Could not delete the hosting of site {Site}; it may still be serving and need removing by hand.",
                    nanoid);
            }
        }

        return Result<SiteError>.Ok();
    }

    private async Task StopPublishAsync(int siteId, int userId, CancellationToken cancellationToken)
    {
        var publish = await deployments.FindInFlightAsync(siteId, cancellationToken);
        var run = publish is null ? null : runs.Get(publish.Nanoid);

        if (run is null || !runs.TryCancel(run.RunId, userId, CancelReason.SiteDeleted)) return;

        // Queued means the runner has not picked it up, and nothing will finish it: the runner finds the row gone and
        // moves on, or — picking it up in this moment — starts on a token that is already cancelled.
        if (publish!.Status == DeploymentStatus.Queued) return;

        try
        {
            await run.Finished.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("The publish {Run} of a site being deleted did not stop in time.", run.RunId);
        }
    }

    private async Task StopTurnAsync(int siteId, int userId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindActiveAsync(siteId, cancellationToken);
        var turn = conversation is null ? null : runs.FindByCorrelation(RunKind.Chat, conversation.Nanoid);

        if (turn is null || !runs.TryCancel(turn.RunId, userId)) return;

        try
        {
            await turn.Finished.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("The turn {Run} on a site being deleted did not stop in time.", turn.RunId);
        }
    }
}
