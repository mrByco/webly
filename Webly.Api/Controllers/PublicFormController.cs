using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Webly.Api.Infrastructure;
using Webly.Services.DTO.Forms;
using Webly.Services.UseCases.Forms;

namespace Webly.Api.Controllers;

/// <summary>
/// Where a published site's forms post. <b>The product's only inbound path from the public internet</b>, and
/// the only endpoint here that expects a browser rather than the Angular app.
///
/// Three decisions are worth reading before changing anything:
///
/// <list type="bullet">
/// <item><b>It takes a form post, not JSON.</b> A published site is a static export with no server of its own,
/// so its forms have to post somewhere else — and an ordinary <c>&lt;form method="post"&gt;</c> to another
/// origin is something browsers have always allowed without a preflight. So this needs no CORS entry, which
/// keeps "there is no CORS and there must not be" true, and the form keeps working with JavaScript switched
/// off, blocked or broken. A contact form is the last thing in a small business's website that should depend
/// on a script loading.</item>
/// <item><b>It redirects the visitor back to the page they came from</b>, resolving the form's own
/// <c>_next</c> path against the <c>Referer</c>, so the site shows its own thank-you rather than one of ours.
/// Not an open redirect worth the name: the target can only be reached by <i>posting</i> from a page that
/// already had the visitor, so anybody able to choose it could have redirected them themselves. A link in an
/// email cannot produce a POST. When there is no usable Referer the answer is a small page of our own, which
/// is also what somebody testing the endpoint by hand sees.</item>
/// <item><b>It is hidden from the OpenAPI document.</b> The Angular client never calls it — the browser
/// submitting it is a stranger's, on somebody else's domain — and a generated TypeScript method for it would
/// be an invitation to call it from the app, which is the one caller it is not for.</item>
/// </list>
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/public/forms")]
public class PublicFormController(SubmitForm submitForm) : ControllerBase
{
    /// <summary>
    /// The site is named in the path rather than in a hidden field, so a form pointed at the wrong site is a
    /// 404 at the URL rather than a message quietly delivered to somebody else's inbox.
    /// </summary>
    [HttpPost("{siteNanoid}")]
    [EnableRateLimiting(RateLimitPolicies.Forms)]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Submit(string siteNanoid, CancellationToken cancellationToken)
    {
        // Explicitly rather than through model binding, because the shape is the agent's to decide: the site's
        // own source says what this form asks for, so there is no DTO to bind to and there must not be one.
        if (!Request.HasFormContentType)
            return Problem(
                "Send this as an ordinary HTML form post.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);

        var form = await Request.ReadFormAsync(cancellationToken);

        var posted = form
            .SelectMany(field => field.Value.Select(value => new SubmittedFormField(field.Key, value ?? string.Empty)))
            .ToList();

        var result = await submitForm.ExecuteAsync(
            siteNanoid,
            posted,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!result.Succeeded) return Failure(result.Error);

        var next = form[FormFieldNames.Next].ToString();
        var back = ReturnUrl(next);

        if (back is null) return Thanks();

        // 303 rather than 302, which is the one status code in this file worth being precise about: it is the
        // only redirect that tells the browser to follow with a GET regardless of what it just sent. Most
        // browsers turn a 302 after a POST into a GET anyway, and "most" is how a form ends up re-posting
        // itself when somebody presses reload.
        Response.Headers.Location = back;

        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>
    /// Where to send the visitor: the form's <c>_next</c>, resolved against the page it was on.
    ///
    /// The path is a relative reference and is checked to be one — a scheme, a host, a leading slash or a
    /// <c>..</c> segment is refused rather than cleaned up, because each of those is a way to aim the redirect
    /// somewhere other than back. What is left can only resolve inside the directory the form itself was
    /// served from.
    /// </summary>
    private string? ReturnUrl(string? next)
    {
        if (!Uri.TryCreate(Request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)) return null;
        if (referer.Scheme is not ("http" or "https")) return null;

        if (string.IsNullOrWhiteSpace(next)) return referer.ToString();

        if (next.Contains("://", StringComparison.Ordinal)
            || next.StartsWith('/')
            || next.StartsWith('\\')
            || next.Contains("..", StringComparison.Ordinal)
            || next.Contains('\n')
            || next.Contains('\r'))
            return referer.ToString();

        return Uri.TryCreate(referer, next, out var resolved) ? resolved.ToString() : referer.ToString();
    }

    /// <summary>
    /// The fallback answer: a page rather than a status code, because whoever sees it is a person who just
    /// pressed a button on somebody's website and needs to know their message arrived. Self-contained, with no
    /// link back — the site's own address is not this endpoint's business, and a wrong one would be worse than
    /// none.
    /// </summary>
    private ContentResult Thanks() => Content(
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="robots" content="noindex">
          <title>Message sent</title>
        </head>
        <body style="margin:0;font:400 16px/1.6 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;background:#faf7f2;color:#2b2320;">
          <main style="max-width:32rem;margin:0 auto;padding:4rem 1rem;text-align:center;">
            <h1 style="font-size:1.5rem;margin:0 0 .5rem;">Thank you &mdash; your message has been sent.</h1>
            <p style="margin:0;color:#6b615c;">You can close this page, or press back to return to the website.</p>
          </main>
        </body>
        </html>
        """,
        "text/html",
        System.Text.Encoding.UTF8);

    /// <summary>
    /// Deliberately plain: a visitor on somebody else's website is the audience, so the sentences say what to do
    /// next rather than naming a rule of ours. A wrong site nanoid answers 404 because that is what it is — the
    /// form in the page is pointed at nothing, which is the site's bug and not the visitor's.
    /// </summary>
    private ActionResult Failure(FormError error) => error switch
    {
        FormError.SiteNotFound => NotFound(new ProblemDetails
        {
            Title = "This form is not connected to a website."
        }),
        FormError.Empty => BadRequest(new ProblemDetails
        {
            Title = "Nothing was filled in, so nothing was sent."
        }),
        FormError.TooLarge => BadRequest(new ProblemDetails
        {
            Title = "That message is too long to send. Please shorten it and try again."
        }),
        FormError.TooMany => StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
        {
            Title = "This website has taken too many messages today. Please try again tomorrow."
        }),
        _ => StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
        {
            Title = "Your message could not be sent."
        })
    };
}
