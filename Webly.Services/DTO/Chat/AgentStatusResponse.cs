namespace Webly.Services.DTO.Chat;

/// <summary>
/// Whether the editing agent exists in this deployment, and which ones. The same pattern as
/// <c>/api/auth/providers</c>: a feature with no credentials is <b>absent</b> rather than broken, and the
/// client asks rather than guessing from a failure.
/// </summary>
public record AgentStatusResponse
{
    public required bool Enabled { get; init; }

    /// <summary>The agents that are configured here. Empty when <c>Enabled</c> is false.</summary>
    public IReadOnlyList<string> Agents { get; init; } = [];
}
