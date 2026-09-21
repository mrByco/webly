using Microsoft.AspNetCore.Mvc;
using Webly.Api.Extensions;
using Webly.Api.Infrastructure;
using Webly.Data.Repositories.Sites;

namespace Webly.Api.Controllers;

/// <summary>
/// Hands the editor the credential its preview frame will need.
///
/// A route of its own rather than one under <c>preview/</c>, because everything under there is a catch-all
/// forwarded to the customer's site: an endpoint at <c>preview/session</c> would quietly shadow a page called
/// <c>/session</c> on somebody's website, and the person whose site that is would never work out why.
///
/// Called by the editor whenever it opens a site — cheap, and it means nobody meets the end of a twelve-hour
/// token halfway through an afternoon. See <see cref="PreviewAccess"/> for what the credential is and why the
/// session cookie cannot be it.
/// </summary>
[ApiController]
[Route("api/sites/{siteNanoid}/preview-access")]
public class PreviewAccessController(ISiteRepository siteRepository, PreviewAccess previewAccess) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Issue(string siteNanoid, CancellationToken cancellationToken)
    {
        var userId = this.GetUserId();
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        // 404 rather than 403, the rule every per-site route follows.
        if (site is null) return NotFound(new ProblemDetails { Title = "That site could not be found." });

        previewAccess.Issue(Response, siteNanoid, userId);

        return NoContent();
    }
}
