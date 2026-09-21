using Microsoft.AspNetCore.Mvc;
using Webly.Api.Extensions;
using Webly.Services.DTO.Domains;
using Webly.Services.UseCases.Domains;

namespace Webly.Api.Controllers;

/// <summary>
/// Custom hostnames for a site. Under the site for the same reason versions are: a hostname belongs to exactly one,
/// and a flat route would invite a lookup that skips the ownership check.
/// </summary>
[ApiController]
[Route("api/sites/{siteNanoid}/domains")]
public class DomainController(
    AddDomain addDomain,
    ListDomains listDomains,
    CheckDomain checkDomain,
    SetPrimaryDomain setPrimaryDomain,
    RemoveDomain removeDomain) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DomainResponse>>> List(string siteNanoid, CancellationToken cancellationToken)
    {
        var result = await listDomains.ExecuteAsync(this.GetUserId(), siteNanoid, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error);
    }

    [HttpPost]
    public async Task<ActionResult<DomainResponse>> Add(
        string siteNanoid,
        AddDomainRequest request,
        CancellationToken cancellationToken)
    {
        var result = await addDomain.ExecuteAsync(this.GetUserId(), siteNanoid, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error, result.Detail);
    }

    /// <summary>
    /// Re-asks the provider. A POST although it reads: it calls an outside service on our account, so it is not
    /// something a browser, a crawler or a prefetch should be able to do by following a link.
    /// </summary>
    [HttpPost("{domainNanoid}/check")]
    public async Task<ActionResult<DomainResponse>> Check(
        string siteNanoid,
        string domainNanoid,
        CancellationToken cancellationToken)
    {
        var result = await checkDomain.ExecuteAsync(this.GetUserId(), siteNanoid, domainNanoid, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error, result.Detail);
    }

    [HttpPost("{domainNanoid}/primary")]
    public async Task<IActionResult> SetPrimary(
        string siteNanoid,
        string domainNanoid,
        CancellationToken cancellationToken)
    {
        var result = await setPrimaryDomain.ExecuteAsync(this.GetUserId(), siteNanoid, domainNanoid, cancellationToken);

        return result.Succeeded ? NoContent() : Failure(result.Error);
    }

    [HttpDelete("{domainNanoid}")]
    public async Task<IActionResult> Remove(
        string siteNanoid,
        string domainNanoid,
        CancellationToken cancellationToken)
    {
        var result = await removeDomain.ExecuteAsync(this.GetUserId(), siteNanoid, domainNanoid, cancellationToken);

        return result.Succeeded ? NoContent() : Failure(result.Error);
    }

    private ActionResult Failure(DomainError error, string? detail = null) => error switch
    {
        DomainError.SiteNotFound => NotFound(new ProblemDetails { Title = "That site could not be found." }),
        DomainError.DomainNotFound => NotFound(new ProblemDetails { Title = "That domain could not be found." }),
        DomainError.InvalidHostname => BadRequest(new ProblemDetails
        {
            Title = "That does not look like a domain name.",
            Detail = "Enter it without https:// and without a path, like example.com or shop.example.com."
        }),
        DomainError.AlreadyConnected => Conflict(new ProblemDetails
        {
            Title = "That domain is already connected to a site.",
            Detail = "If it is one of yours, remove it there first."
        }),
        DomainError.NotVerified => Conflict(new ProblemDetails
        {
            Title = "A domain has to be verified before it can be your main address.",
            Detail = "Add the DNS record shown, then check again."
        }),
        DomainError.ProviderRefused => StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
        {
            Title = "Our hosting provider refused that domain.",
            Detail = detail
        }),
        _ => BadRequest(new ProblemDetails { Title = "That request could not be completed." })
    };
}
