using Microsoft.AspNetCore.Mvc;
using Webly.Api.Extensions;
using Webly.Services.DTO.Assets;
using Webly.Services.UseCases.Assets;

namespace Webly.Api.Controllers;

/// <summary>
/// A site's own photographs. Under the site, like everything else about one.
///
/// There is a <c>GET</c> for the bytes, and it is <b>the editor's, never the site's</b>. A published page
/// serves its own photographs from its own domain, out of the export, and a page that fetched one through
/// here would stop working the moment a visitor who is not signed in loaded it. What this is for is the one thing the
/// site's own URL cannot do: show a picture from a site nobody has published yet, or whose sandbox is asleep,
/// to the person deciding whether to delete it.
/// </summary>
[ApiController]
[Route("api/sites/{siteNanoid}/images")]
public class SiteImageController(
    UploadSiteImages uploadImages,
    ListSiteImages listImages,
    ReadSiteImage readImage,
    DeleteSiteImage deleteImage) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SiteImageResponse>>> List(
        string siteNanoid,
        CancellationToken cancellationToken)
    {
        var result = await listImages.ExecuteAsync(this.GetUserId(), siteNanoid, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error, result.Detail);
    }

    /// <summary>
    /// One image's bytes, for the editor's own thumbnails. See the class comment for why this exists and why
    /// nothing in a customer's site may point at it.
    ///
    /// The file name is a route segment, so a name with a slash in it would not match this route at all — and
    /// the use case refuses one anyway, because a route's shape is not a place to keep a security rule.
    /// </summary>
    /// <param name="version">
    /// Which version's copy of the file, or the head when it is not given. The history's diff asks for a named
    /// one, because the picture a version added is not necessarily the one at that name today and may not be
    /// there at all; the settings screen's thumbnails want the head, which is the question they are asking.
    /// </param>
    [HttpGet("{fileName}")]
    public async Task<IActionResult> Content(
        string siteNanoid,
        string fileName,
        [FromQuery] string? version,
        CancellationToken cancellationToken)
    {
        var result = await readImage.ExecuteAsync(this.GetUserId(), siteNanoid, fileName, version, cancellationToken);

        if (!result.Succeeded) return Failure(result.Error, result.Detail);

        // No caching header, deliberately. A name can be reused by a later upload — the uniqueness is within a
        // version, not for ever — and a stale thumbnail of somebody's replaced photograph is a worse bug than
        // a second request.
        return File(result.Value!.Content, result.Value.ContentType);
    }

    /// <summary>
    /// Removes an image, as its own version. Refused while a page still uses it; the detail names the pages.
    /// </summary>
    [HttpDelete("{fileName}")]
    public async Task<IActionResult> Delete(string siteNanoid, string fileName, CancellationToken cancellationToken)
    {
        var result = await deleteImage.ExecuteAsync(this.GetUserId(), siteNanoid, fileName, cancellationToken);

        return result.Succeeded ? NoContent() : Failure(result.Error, result.Detail);
    }

    /// <summary>
    /// Uploads one or more images and commits them as a version.
    ///
    /// Multipart because that is what a file input sends, and read into memory because the per-file cap is
    /// what makes that safe — <see cref="UploadSiteImages.MaxImageBytes"/> times
    /// <see cref="UploadSiteImages.MaxImagesPerUpload"/> is the ceiling, and the request size limit below is
    /// the one that stops a request never meant to be read.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(UploadSiteImages.MaxImageBytes * UploadSiteImages.MaxImagesPerUpload)]
    public async Task<ActionResult<UploadImagesResponse>> Upload(
        string siteNanoid,
        [FromForm] IFormFileCollection files,
        CancellationToken cancellationToken)
    {
        var uploads = new List<UploadedImage>();

        foreach (var file in files)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);

            uploads.Add(new UploadedImage(file.FileName, stream.ToArray()));
        }

        var result = await uploadImages.ExecuteAsync(this.GetUserId(), siteNanoid, uploads, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : Failure(result.Error, result.Detail);
    }

    /// <summary>
    /// The detail is shown rather than logged for every case but the first: "that is not a JPEG" and "that
    /// file is too big" are both things the person can act on, and both name the file they chose.
    /// </summary>
    private ActionResult Failure(AssetError error, string? detail) => error switch
    {
        // 404 rather than 403, the rule every per-site route follows.
        AssetError.SiteNotFound => NotFound(new ProblemDetails { Title = "That site could not be found." }),
        AssetError.Empty => BadRequest(new ProblemDetails { Title = "No files were attached." }),
        AssetError.NotAnImage => BadRequest(new ProblemDetails
        {
            Title = detail ?? "That file is not an image Webly can put on a website."
        }),
        AssetError.TooLarge => BadRequest(new ProblemDetails { Title = detail ?? "That is too large to upload." }),
        AssetError.NotFound => NotFound(new ProblemDetails { Title = "That image is not in this site." }),
        AssetError.InUse => Conflict(new ProblemDetails
        {
            // Named, because "it is still used" without saying where is not something anybody can act on. The
            // next step is a sentence in the chat, and this is the sentence.
            Title = detail is null
                ? "A page still uses that image. Ask the assistant to take it off the page first."
                : $"A page still uses that image ({detail}). Ask the assistant to take it off the page first."
        }),
        _ => StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
        {
            Title = "Those images could not be added."
        })
    };
}
