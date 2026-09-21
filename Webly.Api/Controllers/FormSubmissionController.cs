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
/// Three verbs, which is the whole of an inbox somebody can work: read them, say they have been read, and
/// throw one away. There is no reply — an enquiry is answered from the owner's own email, where the
/// notification is, with the visitor's address already in the reply-to.
/// </summary>
[ApiController]
[Route("api/sites/{siteNanoid}/submissions")]
public class FormSubmissionController(
    ListFormSubmissions listSubmissions,
    MarkSubmissionsRead markRead,
    DeleteFormSubmission deleteSubmission) : ControllerBase
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

    /// <summary>
    /// Marks everything this site has taken as read. What the Messages screen sends once it has the list on
    /// screen — see <see cref="MarkSubmissionsRead"/> for why it is a POST rather than a side effect of the
    /// GET above, and why it takes no body.
    /// </summary>
    [HttpPost("read")]
    public async Task<IActionResult> MarkRead(string siteNanoid, CancellationToken cancellationToken)
    {
        var result = await markRead.ExecuteAsync(this.GetUserId(), siteNanoid, cancellationToken);

        return result.Succeeded
            ? NoContent()
            : NotFound(new ProblemDetails { Title = "That site could not be found." });
    }

    /// <summary>Throws one message away, for good. Answers 204 for one that is already gone.</summary>
    [HttpDelete("{submissionNanoid}")]
    public async Task<IActionResult> Delete(
        string siteNanoid,
        string submissionNanoid,
        CancellationToken cancellationToken)
    {
        var result = await deleteSubmission.ExecuteAsync(
            this.GetUserId(), siteNanoid, submissionNanoid, cancellationToken);

        return result.Succeeded
            ? NoContent()
            : NotFound(new ProblemDetails { Title = "That site could not be found." });
    }
}
