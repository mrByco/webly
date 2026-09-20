using Webly.Services.Agent.Args;

namespace Webly.Services.DTO.Chat;

/// <summary>
/// What starts a turn. Sent over the hub rather than posted, because the answer is a stream — see
/// docs/agent-plan.md for why the whole chat surface is a hub and not an endpoint.
/// </summary>
public record SendMessageRequest
{
    public required string Message { get; init; }

    /// <summary>Which agent, and in what scope. Polymorphic — see <see cref="BaseAgentArgs"/>.</summary>
    public required BaseAgentArgs Args { get; init; }
}
