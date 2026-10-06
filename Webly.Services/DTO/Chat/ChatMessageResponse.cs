using Webly.Data.Models.Chat;

namespace Webly.Services.DTO.Chat;

public record ChatMessageResponse
{
    public required string Nanoid { get; init; }
    public required MessageRole Role { get; init; }
    public required string Text { get; init; }
    public required DateTime CreatedAt { get; init; }

    /// <summary>The version this turn produced, when it produced one. The link from the chat into the history.</summary>
    public string? ProducedVersionNanoid { get; init; }
}
