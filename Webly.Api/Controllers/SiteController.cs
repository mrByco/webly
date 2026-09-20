using Microsoft.AspNetCore.Mvc;
using Webly.Api.Extensions;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.UseCases.Sites;

namespace Webly.Api.Controllers;

/// <summary>
/// Sites, their versions and their sections.
///
/// Versions live under a site rather than at their own root (<c>/api/sites/{nanoid}/versions/...</c>): a version has
/// no meaning apart from its site, and a flat route would invite a lookup by version nanoid alone — which is exactly
/// the shape that lets somebody read another account's history by guessing.
/// </summary>
[ApiController]
[Route("api/sites")]
public class SiteController(
    CreateSite createSite,
    ListSites listSites,
    GetSite getSite,
    RenameSite renameSite,
    SwitchCurrentSite switchCurrentSite,
    DeleteSite deleteSite,
    ListSiteVersions listSiteVersions,
    GetSiteVersion getSiteVersion,
    RestoreSiteVersion restoreSiteVersion,
    EditSection editSection,
    PreviewSite previewSite) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SiteSummaryResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await listSites.ExecuteAsync(this.GetUserId(), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<SiteSummaryResponse>> Create(
        CreateSiteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await createSite.ExecuteAsync(this.GetUserId(), request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error, result.Detail);
    }

    [HttpGet("{nanoid}")]
    public async Task<ActionResult<SiteDetailResponse>> Get(string nanoid, CancellationToken cancellationToken)
    {
        var result = await getSite.ExecuteAsync(this.GetUserId(), nanoid, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error, result.Detail);
    }

    [HttpPut("{nanoid}")]
    public async Task<IActionResult> Rename(
        string nanoid,
        RenameSiteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await renameSite.ExecuteAsync(this.GetUserId(), nanoid, request, cancellationToken);

        return result.Succeeded ? NoContent() : Failure(result.Error);
    }

    /// <summary>Opens the editor on this site. See <c>User.CurrentSiteId</c>.</summary>
    [HttpPost("{nanoid}/open")]
    public async Task<IActionResult> Open(string nanoid, CancellationToken cancellationToken)
    {
        var result = await switchCurrentSite.ExecuteAsync(this.GetUserId(), nanoid, cancellationToken);

        return result.Succeeded ? NoContent() : Failure(result.Error);
    }

    [HttpDelete("{nanoid}")]
    public async Task<IActionResult> Delete(string nanoid, CancellationToken cancellationToken)
    {
        var result = await deleteSite.ExecuteAsync(this.GetUserId(), nanoid, cancellationToken);

        return result.Succeeded ? NoContent() : Failure(result.Error);
    }

    [HttpGet("{nanoid}/versions")]
    public async Task<ActionResult<IReadOnlyList<SiteVersionResponse>>> Versions(
        string nanoid,
        [FromQuery] int skip,
        [FromQuery] int take,
        CancellationToken cancellationToken)
    {
        var result = await listSiteVersions.ExecuteAsync(this.GetUserId(), nanoid, skip, take == 0 ? 50 : take, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error);
    }

    [HttpGet("{nanoid}/versions/{versionNanoid}")]
    public async Task<ActionResult<SiteVersionResponse>> Version(
        string nanoid,
        string versionNanoid,
        CancellationToken cancellationToken)
    {
        var result = await getSiteVersion.ExecuteAsync(this.GetUserId(), nanoid, versionNanoid, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error);
    }

    /// <summary>
    /// Restores an earlier version by copying it forward. A POST rather than a PUT on the site: it creates a version,
    /// and the thing it creates is what it returns.
    /// </summary>
    [HttpPost("{nanoid}/versions/{versionNanoid}/restore")]
    public async Task<ActionResult<SiteVersionResponse>> Restore(
        string nanoid,
        string versionNanoid,
        CancellationToken cancellationToken)
    {
        var result = await restoreSiteVersion.ExecuteAsync(this.GetUserId(), nanoid, versionNanoid, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error, result.Detail);
    }

    /// <summary>
    /// A hand edit from the property editor. Returns the version it created, so the client's next edit patches the
    /// document it can see rather than one it assumed.
    /// </summary>
    [HttpPost("{nanoid}/sections")]
    public async Task<ActionResult<SiteVersionResponse>> EditSection(
        string nanoid,
        EditSectionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await editSection.ExecuteAsync(this.GetUserId(), nanoid, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error, result.Detail);
    }

    /// <summary>
    /// The rendered HTML of one page, for the editor's preview iframe. Text rather than JSON, because the iframe loads
    /// it directly — wrapping the page in a JSON string only to unwrap it into <c>srcdoc</c> would cost a copy of the
    /// whole document and lose the browser's own caching.
    ///
    /// The stylesheet is linked absolutely here rather than relatively, because every page of a site is previewed from
    /// this one URL — see <c>RenderContext.StylesheetUrl</c> — and <see cref="PreviewStylesheet"/> serves it.
    /// </summary>
    [HttpGet("{nanoid}/preview")]
    [Produces("text/html")]
    public async Task<IActionResult> Preview(
        string nanoid,
        [FromQuery] string? version,
        [FromQuery] string? page,
        CancellationToken cancellationToken)
    {
        var stylesheetUrl = Url.Action(nameof(PreviewStylesheet), new { nanoid, version });

        var result = await previewSite.ExecuteAsync(
            this.GetUserId(), nanoid, version, page ?? "/", stylesheetUrl, cancellationToken);

        return result.Succeeded
            ? Content(result.Value!, "text/html; charset=utf-8")
            : Failure(result.Error, result.Detail);
    }

    /// <summary>The preview's stylesheet. See <see cref="Preview"/>.</summary>
    [HttpGet("{nanoid}/preview.css")]
    [Produces("text/css")]
    public async Task<IActionResult> PreviewStylesheet(
        string nanoid,
        [FromQuery] string? version,
        CancellationToken cancellationToken)
    {
        var result = await previewSite.StylesheetAsync(this.GetUserId(), nanoid, version, cancellationToken);

        return result.Succeeded
            ? Content(result.Value!, "text/css; charset=utf-8")
            : Failure(result.Error, result.Detail);
    }

    /// <summary>
    /// The section catalogue. Anonymous would be harmless, but it is not public information either — it is the shape
    /// of the product's editor, and there is no screen that needs it before sign-in.
    /// </summary>
    [HttpGet("catalogue")]
    public ActionResult<IReadOnlyList<SectionSchemaResponse>> Catalogue() => Ok(SiteMapper.Catalogue());

    /// <summary>
    /// The one place a <see cref="SiteError"/> becomes a status code.
    ///
    /// <c>NotFound</c> answers 404 for both "no such site" and "not yours" — a 403 would confirm that a guessed nanoid
    /// names a real site, and a site's existence is its owner's business.
    /// </summary>
    private ActionResult Failure(SiteError error, string? detail = null) => error switch
    {
        SiteError.NotFound => NotFound(new ProblemDetails { Title = "That site could not be found." }),
        SiteError.VersionNotFound => NotFound(new ProblemDetails { Title = "That version could not be found." }),
        SiteError.LimitReached => Conflict(new ProblemDetails
        {
            Title = "You have reached the number of sites your plan includes.",
            Detail = "Delete one you no longer need, or upgrade."
        }),
        SiteError.InvalidName => BadRequest(new ProblemDetails { Title = "A site needs a name of at most 80 characters." }),
        SiteError.InvalidDocument => BadRequest(new ProblemDetails
        {
            Title = "That change would leave the site in a state it cannot be published from.",
            Detail = detail
        }),
        _ => BadRequest(new ProblemDetails { Title = "That request could not be completed." })
    };
}
