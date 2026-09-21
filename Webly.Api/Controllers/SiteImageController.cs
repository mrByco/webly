using Microsoft.AspNetCore.Mvc;
using Webly.Api.Extensions;
using Webly.Services.DTO.Assets;
using Webly.Services.UseCases.Assets;

namespace Webly.Api.Controllers;

/// <summary>
/// A site's own photographs. Under the site, like everything else about one.
///
/// There is no <c>GET</c> for the bytes here, and that is not an omission: an image lives in the site's
/// repository, so the preview serves it from the site's own dev server and the published site serves it from
/// its own domain. A second way to fetch it through Webly's origin would be a second URL for the same picture,
/// and the one the customer's pages use is the one that has to work.
/// </summary>
[ApiController]
[Route("api/sites/{siteNanoid}/images")]
public class SiteImageController(UploadSiteImages uploadImages, ListSiteImages listImages) : ControllerBase
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
        _ => StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
        {
            Title = "Those images could not be added."
        })
    };
}
