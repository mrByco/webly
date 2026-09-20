using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Webly.Api.Extensions;
using Webly.Services.Agent;
using Webly.Services.DTO.Chat;
using Webly.Services.DTO.Sites;
using Webly.Services.UseCases.Chat;

namespace Webly.Api.Controllers;

/// <summary>
/// The chat's <b>non-streaming</b> half: load the thread, start a new one, ask whether the agent exists at all.
/// Sending a message is a hub call, because its answer is a stream — see docs/agent-plan.md.
/// </summary>
[ApiController]
[Route("api/sites/{siteNanoid}/chat")]
public class ChatController(
    GetChat getChat,
    ArchiveChat archiveChat,
    IOptions<AgentOptions> agentOptions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ConversationResponse>> Get(string siteNanoid, CancellationToken cancellationToken)
    {
        var result = await getChat.ExecuteAsync(this.GetUserId(), siteNanoid, cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            : NotFound(new ProblemDetails { Title = "That site could not be found." });
    }

    /// <summary>"New chat": archives the open thread. The next message starts a fresh one.</summary>
    [HttpPost("archive")]
    public async Task<IActionResult> Archive(string siteNanoid, CancellationToken cancellationToken)
    {
        var result = await archiveChat.ExecuteAsync(this.GetUserId(), siteNanoid, cancellationToken);

        return result.Succeeded
            ? NoContent()
            : NotFound(new ProblemDetails { Title = "That site could not be found." });
    }

    /// <summary>
    /// Whether this deployment has an agent. The same shape as <c>/api/auth/providers</c>: an unconfigured feature is
    /// absent rather than broken, and the client asks rather than inferring it from a failed call.
    ///
    /// Route is <c>/api/sites/{siteNanoid}/chat/status</c> for consistency, although the answer is deployment-wide —
    /// the alternative is one endpoint at a different root for one boolean.
    /// </summary>
    [HttpGet("status")]
    public ActionResult<AgentStatusResponse> Status() =>
        Ok(new AgentStatusResponse { Enabled = agentOptions.Value.IsConfigured });
}
