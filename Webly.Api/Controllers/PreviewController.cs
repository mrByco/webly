using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webly.Api.Extensions;
using Webly.Api.Infrastructure;
using Webly.Data.Repositories.Sites;
using Webly.Services.Services.Workspaces;
using Yarp.ReverseProxy.Forwarder;

namespace Webly.Api.Controllers;

/// <summary>
/// The editor's preview: a reverse proxy from this origin to the site's own <c>next dev</c> in its sandbox.
///
/// <b>The preview is the real dev server.</b> Not a re-render, not a snapshot — the same process the agent's edits
/// land in, with hot reload, so what somebody approves is what the next build will produce. That is the whole
/// reason a workspace stays warm.
///
/// Four things make it safe to point a browser at somebody else's machine:
///
/// <list type="bullet">
/// <item><b>The browser never reaches the sandbox.</b> It talks to Webly and Webly talks to the sandbox, so the
/// sandbox's URL and token stay server-side and there is no CORS.</item>
/// <item><b>Ownership is checked per request</b>, by the same repository method as everything else. A site's
/// preview is not a public URL with a guessable id.</item>
/// <item><b>The framed document has no origin of Webly's.</b> The editor sandboxes the frame, so the customer's
/// own page — written by a coding agent — cannot call this app's API as the person watching it. This used to be
/// the opposite: being same-origin was written down here as the thing that made the preview safe, and it was
/// what made it dangerous. See <see cref="PreviewAccess"/>, which is the credential that replaces the session
/// cookie once the frame has no origin to send one from.</item>
/// <item><b>WebSockets are forwarded</b>, because hot reload is one — without it the preview loads once and then
/// silently stops updating, which looks exactly like the agent not working.</item>
/// </list>
///
/// YARP's direct forwarder rather than a route in the reverse-proxy configuration: the destination is per site and
/// per session, so there is no static cluster to configure, and this is the one case the forwarder API exists for.
/// </summary>
[ApiController]
[Route("api/sites/{siteNanoid}/preview")]
public class PreviewController(
    IHttpForwarder forwarder,
    ISiteRepository siteRepository,
    ISiteWorkspaceRegistry workspaces,
    PreviewAccess previewAccess,
    PreviewForwarder client,
    ILogger<PreviewController> logger) : ControllerBase
{
    /// <summary>
    /// Everything under the route, forwarded. The catch-all is the point: a Next.js page asks for its own
    /// chunks, its fonts and its hot-reload socket, all relative to wherever the document was served from.
    ///
    /// <b>Every method, deliberately</b> — a route with no verb attribute accepts all of them. A proxy that
    /// answers 405 to a <c>HEAD</c> for a font, or to whatever the framework adds next, is a proxy with a list
    /// to maintain; and the page being served is the customer's, so the list is not ours to predict.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// Out of the OpenAPI document, and it has to be: Swashbuckle refuses an action with no explicit method
    /// ("Ambiguous HTTP method for action"), so accepting every verb and describing the endpoint are mutually
    /// exclusive. Describing it is the one to give up. It is not an operation a generated client calls — the
    /// editor puts this URL in an <c>iframe</c>'s <c>src</c> and the browser asks for everything under it —
    /// and a catch-all proxy has no request or response shape to generate anyway.
    /// </para>
    /// <para>
    /// <c>[AllowAnonymous]</c> is deliberate and is the only one under a site. The frame is sandboxed, so
    /// everything the framed document asks for arrives without the session cookie — the browser treats an
    /// opaque origin as cross-site and withholds it. The credential is <see cref="PreviewAccess"/>'s own
    /// cookie, and the check below is the same ownership query every other per-site route makes. A request
    /// with neither credential gets the same 404 a stranger gets.
    /// </para>
    /// </remarks>
    [Route("{**path}")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Forward(string siteNanoid, CancellationToken cancellationToken)
    {
        // A font, and only a font, is let through without a credential. See `IsFont` for why it has to be.
        if (IsFont(Request))
        {
            Response.Headers.AccessControlAllowOrigin = "*";
        }
        else
        {
            // The preview cookie first, because it is the one the frame can send. The session is the fallback,
            // for the top-level navigation that loads the frame and for anybody opening the URL directly.
            var userId = previewAccess.UserFor(Request, siteNanoid) ?? this.GetUserIdUnverified();

            if (userId is null) return NotFound();

            var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId.Value, cancellationToken);

            if (site is null) return NotFound();
        }

        var workspace = workspaces.Find(siteNanoid);

        if (workspace is null)
            // 503 with a sentence rather than starting one: a workspace takes tens of seconds, and an <iframe>
            // that hangs for that long looks broken. The editor starts it by sending a message or by asking for
            // it explicitly, and shows its progress while it does.
            return Unavailable(
                StatusCodes.Status503ServiceUnavailable,
                "Your site's preview is not running.",
                "Send a message to wake it up.",
                retry: false);

        var destination = new Uri(workspace.Sandbox.AgentUrl, "preview/").ToString();

        var transformer = new PreviewTransformer(workspace.Sandbox.AgentToken, $"/api/sites/{siteNanoid}/preview");

        var error = await forwarder.SendAsync(
            HttpContext, destination, client.Client, new ForwarderRequestConfig
            {
                // Generous: a cold page in a dev server compiles on first request, and the compile is the wait.
                ActivityTimeout = TimeSpan.FromMinutes(2)
            }, transformer);

        // The sandbox answered for the dev server rather than from it. That is not a forwarding error — the
        // forward worked — so it has to be noticed here, or its JSON body goes straight into the iframe.
        if (transformer.DevServerState is { } state && !Response.HasStarted)
            return state == "stopped"
                ? Unavailable(
                    StatusCodes.Status503ServiceUnavailable,
                    "Your site's preview has stopped.",
                    "Send a message and it will start again.",
                    retry: false)
                : Unavailable(
                    StatusCodes.Status502BadGateway,
                    "Your site is starting up.",
                    "This page will come back on its own.",
                    retry: true);

        if (error != ForwarderError.None)
        {
            logger.LogWarning("Preview forwarding for {Site} failed: {Error}", siteNanoid, error);

            // The response may already have started, in which case there is nothing left to say — returning a
            // result here would throw over the top of a half-written body.
            if (!Response.HasStarted)
            {
                // Which of the two it is, asked rather than assumed. A forward fails while the dev server is
                // recompiling — seconds, and a page that retries is exactly right — and it also fails when the
                // dev server is gone, where a page that retries every second for ever tells somebody their site
                // is compiling until they give up on it. The sandbox knows the difference now.
                var log = await workspace.Sandbox.ReadDevServerLogAsync(cancellationToken: cancellationToken);

                if (!log.Running)
                    return Unavailable(
                        StatusCodes.Status503ServiceUnavailable,
                        "Your site's preview has stopped.",
                        "Send a message and it will start again.",
                        retry: false);

                return Unavailable(
                    StatusCodes.Status502BadGateway,
                    "Your site is compiling.",
                    "This page will come back on its own.",
                    retry: true);
            }
        }

        return Empty;
    }

    /// <summary>
    /// Whether this is a request for one of the site's own font files, which is the one thing a sandboxed
    /// frame cannot ask for with a credential.
    ///
    /// <b>A font is always fetched with CORS</b>, in credentials mode <c>same-origin</c> — and the framed
    /// document's origin is opaque, so nothing is same-origin to it and no cookie is ever attached. Scripts,
    /// stylesheets, images and the hot-reload socket are all fetched in modes that do send one, which is why
    /// they kept working and only the typeface did not: the preview rendered in a fallback font while the
    /// published site rendered in Inter, and "what you approve is what you publish" is the preview's whole
    /// job.
    ///
    /// So this one shape of request is served on the nanoid alone. What it exposes is a copy of a public
    /// typeface, under a path this build put it at, to somebody who already knows an unguessable id — not the
    /// site's pages, not its code, and nothing of its owner's. Method, directory and extension all have to
    /// match; anything else takes the credential.
    ///
    /// <b>The real answer is a separate origin for previews</b> — <c>{id}.preview.webly.site</c> — where the
    /// frame's own origin serves its own assets and none of this arises. That needs a wildcard record and a
    /// certificate; see <see cref="PreviewAccess"/>.
    /// </summary>
    private static bool IsFont(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return false;

        var path = request.Path.Value ?? string.Empty;

        // Next puts what `next/font` produces under `_next/static/media`, beside images — hence the extension
        // check as well as the directory.
        if (!path.Contains("/_next/static/media/", StringComparison.Ordinal)) return false;

        return path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What to answer when there is no dev server to forward to.
    ///
    /// <b>HTML when a browser asked for a page</b>, because this endpoint's usual caller is an
    /// <c>&lt;iframe&gt;</c> in the editor, and what it renders is whatever comes back: a <c>ProblemDetails</c>
    /// body puts raw JSON on screen inside somebody's preview. A retrying page instead, since the interesting
    /// case is transient — the dev server recompiling after a commit takes a couple of seconds and the frame
    /// was pointed at it at exactly that moment.
    ///
    /// A request for anything else — a chunk, a stylesheet, the hot-reload socket — still gets the problem
    /// document. Those are not shown to anybody, and a client that is not a browser should read a status code
    /// and a machine-readable body rather than a page of markup.
    /// </summary>
    private IActionResult Unavailable(int status, string title, string detail, bool retry)
    {
        var wantsHtml = Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

        if (!wantsHtml)
            return StatusCode(status, new ProblemDetails { Title = title, Detail = detail });

        Response.StatusCode = status;

        // No framework, no stylesheet, no script beyond the reload: this page is served when the thing that
        // serves pages is not answering, so it cannot depend on anything.
        var refresh = retry ? """<meta http-equiv="refresh" content="2">""" : string.Empty;

        // `$$"""` so that the stylesheet's braces are literal and the two values interpolate as `{{name}}`.
        return Content(
            $$"""
            <!doctype html>
            <html lang="en">
              <head>
                <meta charset="utf-8">
                {{refresh}}
                <title>{{title}}</title>
                <style>
                  body { margin: 0; display: grid; place-items: center; height: 100vh;
                         font: 14px/1.5 system-ui, sans-serif; color: #6b6b6b; background: #fbfbfc; }
                  p { margin: 0; text-align: center; }
                  strong { display: block; color: #2b2b2b; font-size: 15px; margin-bottom: 4px; }
                </style>
              </head>
              <body><p><strong>{{title}}</strong>{{detail}}</p></body>
            </html>
            """,
            "text/html; charset=utf-8");
    }

    /// <summary>
    /// Rewrites the request on its way to the sandbox: strip the route prefix, add the sandbox's token, and drop
    /// anything of ours the dev server has no business seeing.
    /// </summary>
    /// <summary>
    /// Rewrites the request for the sandbox, and notices when the sandbox is answering <i>about</i> the dev server
    /// instead of <i>for</i> it.
    /// </summary>
    private sealed class PreviewTransformer(string token, string prefix) : HttpTransformer
    {
        /// <summary>
        /// <c>starting</c>, <c>stopped</c>, or null when the answer came from the site itself. Read by the action
        /// after the forward, which is the only place that can put a page there instead.
        /// </summary>
        public string? DevServerState { get; private set; }

        public override async ValueTask TransformRequestAsync(
            HttpContext context,
            HttpRequestMessage request,
            string destinationPrefix,
            CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(context, request, destinationPrefix, cancellationToken);

            var path = context.Request.Path.Value ?? string.Empty;
            var relative = path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? path[prefix.Length..].TrimStart('/')
                : path.TrimStart('/');

            request.RequestUri = new Uri($"{destinationPrefix.TrimEnd('/')}/{relative}{context.Request.QueryString}");

            // Our session cookie must not travel on: the sandbox has no use for it, and forwarding a credential
            // to another machine because it happened to be on the request is how one leaks.
            request.Headers.Remove("Cookie");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        /// <summary>
        /// Lets the sandbox's own "there is no dev server here" answer through to the action rather than into the
        /// frame. <c>false</c> means the body is not copied — nothing has been written yet, so the action can still
        /// answer with a page.
        /// </summary>
        public override async ValueTask<bool> TransformResponseAsync(
            HttpContext context,
            HttpResponseMessage? response,
            CancellationToken cancellationToken)
        {
            if (response is not null
                && response.Headers.TryGetValues("x-webly-dev", out var values))
            {
                DevServerState = values.FirstOrDefault();

                return false;
            }

            return await base.TransformResponseAsync(context, response, cancellationToken);
        }
    }
}
