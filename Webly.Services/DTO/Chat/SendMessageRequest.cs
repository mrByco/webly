namespace Webly.Services.DTO.Chat;

/// <summary>
/// What starts a turn. Sent over the hub rather than posted, because the answer is a stream — see
/// docs/agent-plan.md for why the whole chat surface is a hub and not an endpoint.
/// </summary>
public record SendMessageRequest
{
    public required string SiteNanoid { get; init; }

    public required string Message { get; init; }

    /// <summary>
    /// Which coding agent, when the person has a preference (<c>claude-code</c>, <c>opencode</c>). Null means
    /// the deployment's default — which is the normal case: the product does not put this choice on screen, and
    /// the field exists so that trying the other one is a request parameter rather than a deployment.
    /// </summary>
    public string? Agent { get; init; }
}
