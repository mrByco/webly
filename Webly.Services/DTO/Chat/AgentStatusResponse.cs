namespace Webly.Services.DTO.Chat;

/// <summary>
/// Whether the editor agent exists in this deployment. The same pattern as <c>/api/auth/providers</c>: a feature
/// with no credentials is <b>absent</b> rather than broken, and the client asks rather than guessing from a failure.
/// </summary>
public record AgentStatusResponse
{
    public required bool Enabled { get; init; }
}
