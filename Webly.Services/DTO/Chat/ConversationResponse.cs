namespace Webly.Services.DTO.Chat;

/// <summary>The open thread for a site, as the editor loads it.</summary>
public record ConversationResponse
{
    public required string Nanoid { get; init; }
    public string? Title { get; init; }
    public required IReadOnlyList<ChatMessageResponse> Messages { get; init; }

    /// <summary>
    /// The run id of a turn that is still going, if there is one. What lets a reloaded editor re-attach to a turn
    /// in progress instead of showing a finished-looking thread with a half-written answer missing.
    /// </summary>
    public string? ActiveRunId { get; init; }
}
