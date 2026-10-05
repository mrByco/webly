using Microsoft.AspNetCore.Mvc;
using Webly.Api.Extensions;
using Webly.Services.DTO.Usage;
using Webly.Services.UseCases.Usage;

namespace Webly.Api.Controllers;

/// <summary>What the platform costs to run, for the people who run it.</summary>
[ApiController]
[Route("api/admin/usage")]
public class UsageController(GetUsageReport getUsageReport) : ControllerBase
{
    [HttpGet]
    // Both, explicitly: declaring only the 403 stops the 200's body being inferred from ActionResult<T>, and the
    // generated client then types the report as void.
    [ProducesResponseType<UsageReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UsageReportResponse>> Get(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await getUsageReport.ExecuteAsync(this.GetUserId(), days, cancellationToken);

        // 403 rather than the 404 a site route answers: there is nothing secret about this route existing, and a
        // signed-in person who is not an administrator is owed the honest answer.
        return result.Succeeded
            ? Ok(result.Value)
            : StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Title = "This report is for administrators." });
    }
}
