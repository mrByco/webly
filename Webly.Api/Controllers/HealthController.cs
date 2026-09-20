using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Webly.Api.Controllers;

public record HealthResponse(string Status, string DbSource);

[ApiController]
[AllowAnonymous]
[Route("health")]
public class HealthController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public ActionResult<HealthResponse> Get()
    {
        var dbSource = configuration["Database:ActiveSource"] ?? "unknown";
        return Ok(new HealthResponse("ok", dbSource));
    }
}
