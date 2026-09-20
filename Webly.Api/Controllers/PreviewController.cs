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
/// Three things make it safe to point a browser at somebody else's machine:
///
/// <list type="bullet">
/// <item><b>It is same-origin.</b> The sandbox is never given to the browser; the browser talks to Webly and Webly
/// talks to the sandbox. So the session cookie is enough, there is no CORS, and the sandbox's own URL and token
/// stay server-side.</item>
/// <item><b>Ownership is checked per request</b>, by the same repository method as everything else. A site's
/// preview is not a public URL with a guessable id.</item>
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
    /// Out of the OpenAPI document, and it has to be: Swashbuckle refuses an action with no explicit method
    /// ("Ambiguous HTTP method for action"), so accepting every verb and describing the endpoint are mutually
    /// exclusive. Describing it is the one to give up. It is not an operation a generated client calls — the
    /// editor puts this URL in an <c>iframe</c>'s <c>src</c> and the browser asks for everything under it —
    /// and a catch-all proxy has no request or response shape to generate anyway.
    [Route("{**path}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Forward(string siteNanoid, CancellationToken cancellationToken)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, this.GetUserId(), cancellationToken);

        if (site is null) return NotFound();

        var workspace = workspaces.Find(siteNanoid);

        if (workspace is null)
            // 503 with a sentence rather than starting one: a workspace takes tens of seconds, and an <iframe>
            // that hangs for that long looks broken. The editor starts it by sending a message or by asking for
            // it explicitly, and shows its progress while it does.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Your site's preview is not running.",
                Detail = "Send a message to wake it up."
            });

        var destination = new Uri(workspace.Sandbox.AgentUrl, "preview/").ToString();

        var transformer = new PreviewTransformer(workspace.Sandbox.AgentToken, $"/api/sites/{siteNanoid}/preview");

        var error = await forwarder.SendAsync(
            HttpContext, destination, client.Client, new ForwarderRequestConfig
            {
                // Generous: a cold page in a dev server compiles on first request, and the compile is the wait.
                ActivityTimeout = TimeSpan.FromMinutes(2)
            }, transformer);

        if (error != ForwarderError.None)
        {
            logger.LogWarning("Preview forwarding for {Site} failed: {Error}", siteNanoid, error);

            // The response may already have started, in which case there is nothing left to say — returning a
            // result here would throw over the top of a half-written body.
            if (!Response.HasStarted)
                return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Title = "The preview is not answering.",
                    Detail = "It may still be compiling. Wait a moment and reload."
                });
        }

        return Empty;
    }

    /// <summary>
    /// Rewrites the request on its way to the sandbox: strip the route prefix, add the sandbox's token, and drop
    /// anything of ours the dev server has no business seeing.
    /// </summary>
    private sealed class PreviewTransformer(string token, string prefix) : HttpTransformer
    {
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
    }
}
