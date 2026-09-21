using Microsoft.AspNetCore.Mvc;
using Webly.Api.Extensions;
using Webly.Services.DTO.Forms;
using Webly.Services.UseCases.Forms;

namespace Webly.Api.Controllers;

/// <summary>
/// What visitors have sent from a site, for its owner. Under the site, like everything else about one — the
/// submissions of two sites are never one list, and a flat route would invite a lookup by a submission's own
/// nanoid with no owner in it.
///
/// Read-only, and that is the whole surface for now. Deleting one and marking one read are both reasonable and
/// both need a decision about what the editor does with the state; a list that is honest about what arrived is
/// the part somebody cannot do without.
/// </summary>
[ApiController]
[Route("api/sites/{siteNanoid}/submissions")]
public class FormSubmissionController(ListFormSubmissions listSubmissions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FormSubmissionResponse>>> List(
        string siteNanoid,
        CancellationToken cancellationToken)
    {
        var result = await listSubmissions.ExecuteAsync(this.GetUserId(), siteNanoid, cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            // Not 403: a 403 would confirm that a guessed nanoid names a real site. The same rule as every
            // other per-site route, and SiteIsolationTests is what keeps it true.
            : NotFound(new ProblemDetails { Title = "That site could not be found." });
    }
}
