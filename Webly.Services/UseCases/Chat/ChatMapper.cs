using Webly.Data.Models.Chat;
using Webly.Services.DTO.Chat;

namespace Webly.Services.UseCases.Chat;

public static class ChatMapper
{
    public static ChatMessageResponse ToResponse(ConversationMessage message) => new()
    {
        Nanoid = message.Nanoid,
        Role = message.Role,
        Text = message.Text,
        CreatedAt = message.CreatedAt,
        Parts = message.Parts,
        ProducedVersionNanoid = message.ProducedVersion?.Nanoid
    };
}
