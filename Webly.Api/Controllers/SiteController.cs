using Microsoft.AspNetCore.Mvc;
using Webly.Api.Extensions;
using Webly.Services.DTO.Sites;
using Webly.Services.UseCases.Sites;

namespace Webly.Api.Controllers;

/// <summary>
/// Sites, their versions and their source.
///
/// Versions and files live under a site (<c>/api/sites/{nanoid}/versions/…</c>) rather than at their own root: a
/// version has no meaning apart from its site, and a flat route would invite a lookup by version nanoid alone —
/// which is exactly the shape that lets somebody read another account's history by guessing.
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
    ReadSiteFiles readSiteFiles,
    ExportSite exportSite) : ControllerBase
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

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error);
    }

    [HttpGet("{nanoid}")]
    public async Task<ActionResult<SiteDetailResponse>> Get(string nanoid, CancellationToken cancellationToken)
    {
        var result = await getSite.ExecuteAsync(this.GetUserId(), nanoid, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error);
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
        var result = await listSiteVersions.ExecuteAsync(
            this.GetUserId(), nanoid, skip, take == 0 ? 50 : take, cancellationToken);

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
    /// What one version changed, as a unified diff. Text rather than JSON: it is a diff, every client already
    /// knows how to colour one, and wrapping it in a string field only to unwrap it costs a copy of it.
    /// </summary>
    [HttpGet("{nanoid}/versions/{versionNanoid}/diff")]
    [Produces("text/plain")]
    public async Task<IActionResult> Diff(string nanoid, string versionNanoid, CancellationToken cancellationToken)
    {
        var result = await readSiteFiles.DiffAsync(this.GetUserId(), nanoid, versionNanoid, cancellationToken);

        return result.Succeeded
            ? Content(result.Value!, "text/plain; charset=utf-8")
            : Failure(result.Error);
    }

    /// <summary>
    /// Restores an earlier version by writing its tree forward. A POST rather than a PUT on the site: it creates
    /// a version, and the thing it creates is what it returns.
    /// </summary>
    [HttpPost("{nanoid}/versions/{versionNanoid}/restore")]
    public async Task<ActionResult<SiteVersionResponse>> Restore(
        string nanoid,
        string versionNanoid,
        CancellationToken cancellationToken)
    {
        var result = await restoreSiteVersion.ExecuteAsync(this.GetUserId(), nanoid, versionNanoid, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error);
    }

    /// <summary>
    /// The site's files at a commit — the head by default. The editor's code view reads this, because "you never
    /// have to touch the code" is not "you are not allowed to see it".
    /// </summary>
    [HttpGet("{nanoid}/files")]
    public async Task<ActionResult<IReadOnlyList<SiteFileEntryResponse>>> Files(
        string nanoid,
        [FromQuery] string? version,
        CancellationToken cancellationToken)
    {
        var result = await readSiteFiles.ListAsync(this.GetUserId(), nanoid, version, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error);
    }

    /// <summary>
    /// One file. The path is a query parameter rather than part of the route, because a route with a
    /// <c>{**path}</c> catch-all makes <c>src/app/page.tsx</c> ambiguous against the routes above it, and
    /// escaping it per client is the kind of thing that works until somebody's file has a hash in its name.
    /// </summary>
    [HttpGet("{nanoid}/file")]
    public async Task<ActionResult<SiteFileResponse>> File(
        string nanoid,
        [FromQuery] string path,
        [FromQuery] string? version,
        CancellationToken cancellationToken)
    {
        var result = await readSiteFiles.ReadAsync(this.GetUserId(), nanoid, path, version, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error);
    }

    /// <summary>
    /// The site's whole repository, as a git bundle to download.
    ///
    /// The one endpoint here that answers with a file rather than JSON, and the reason it exists is the
    /// product's central claim: a site is real source code and it belongs to the person who paid for it.
    /// `git clone` on what this returns gives them a working project with the entire history — so leaving is
    /// possible, which is what makes staying a choice.
    /// </summary>
    [HttpGet("{nanoid}/export")]
    [Produces("application/x-git-bundle")]
    public async Task<IActionResult> Export(string nanoid, CancellationToken cancellationToken)
    {
        var result = await exportSite.ExecuteAsync(this.GetUserId(), nanoid, cancellationToken);

        if (!result.Succeeded) return Failure(result.Error);

        // A download rather than something the browser tries to render. The client reaches this with a plain
        // link rather than through the generated API client, because a link is what a download is.
        return File(result.Value!.Content, "application/x-git-bundle", result.Value.FileName);
    }

    /// <summary>
    /// The one place a <see cref="SiteError"/> becomes a status code.
    ///
    /// <c>NotFound</c> answers 404 for both "no such site" and "not yours" — a 403 would confirm that a guessed
    /// nanoid names a real site, and a site's existence is its owner's business.
    /// </summary>
    private ActionResult Failure(SiteError error) => error switch
    {
        SiteError.NotFound => NotFound(new ProblemDetails { Title = "That site could not be found." }),
        SiteError.VersionNotFound => NotFound(new ProblemDetails { Title = "That version could not be found." }),
        SiteError.FileNotFound => NotFound(new ProblemDetails { Title = "That file is not in this version." }),
        SiteError.LimitReached => Conflict(new ProblemDetails
        {
            Title = "You have reached the number of sites your plan includes.",
            Detail = "Delete one you no longer need, or upgrade."
        }),
        SiteError.InvalidName => BadRequest(new ProblemDetails { Title = "A site needs a name of at most 80 characters." }),
        // No Detail, deliberately. The detail here is git's own stderr, and RepositoryException says in its own
        // comment that its message is for a log rather than for a customer — it names server-side paths, which
        // a site's owner has no use for and should not be handed. The use case logs it instead.
        SiteError.RepositoryFailed => StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
        {
            Title = "Your site's history could not be read.",
            Detail = "This has been recorded. Nothing has been lost — please try again."
        }),
        SiteError.WorkspaceUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
        {
            Title = "No build machine is available right now.",
            Detail = "Your site is safe — try again in a moment."
        }),
        _ => BadRequest(new ProblemDetails { Title = "That request could not be completed." })
    };
}
