using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Webly.Api.Extensions;
using Webly.Api.Infrastructure;
using Webly.Services.DTO.Deployments;
using Webly.Services.UseCases.Deployments;

namespace Webly.Api.Controllers;

/// <summary>
/// Publishing. Two endpoints, because that is the whole of it from the client's side: ask, then watch — the watching
/// happens on the realtime hub, and the list is what a reloaded page reads instead.
/// </summary>
[ApiController]
[Route("api/sites/{siteNanoid}/deployments")]
public class DeploymentController(PublishSite publishSite, ListDeployments listDeployments) : ControllerBase
{
    /// <summary>
    /// Queues a publish and answers immediately with the row. 202, not 200: nothing is live yet, and a status code
    /// that says "done" would be the client's excuse to show a success toast for work that has not started.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Deploy)]
    public async Task<ActionResult<DeploymentResponse>> Publish(string siteNanoid, CancellationToken cancellationToken)
    {
        var result = await publishSite.ExecuteAsync(this.GetUserId(), siteNanoid, cancellationToken);

        return result.Succeeded
            ? Accepted(result.Value)
            : Failure(result.Error);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeploymentResponse>>> List(
        string siteNanoid,
        CancellationToken cancellationToken)
    {
        var result = await listDeployments.ExecuteAsync(this.GetUserId(), siteNanoid, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error);
    }

    private ActionResult Failure(DeployError error) => error switch
    {
        DeployError.SiteNotFound => NotFound(new ProblemDetails { Title = "That site could not be found." }),
        DeployError.NothingToPublish => Conflict(new ProblemDetails
        {
            Title = "This site is already published as it is.",
            Detail = "Make a change first."
        }),
        DeployError.PublishingUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
        {
            Title = "Publishing is not set up in this environment.",
            Detail = "Your changes are saved; ask an administrator to configure hosting."
        }),
        DeployError.InvalidDocument => BadRequest(new ProblemDetails
        {
            Title = "This site cannot be published as it is.",
            Detail = "Open the editor and check the page for anything incomplete."
        }),
        _ => BadRequest(new ProblemDetails { Title = "That request could not be completed." })
    };
}
