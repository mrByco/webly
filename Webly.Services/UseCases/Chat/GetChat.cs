using Webly.Data.Models.Chat;
using Webly.Data.Repositories.Chat;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Chat;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Realtime;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Realtime;

namespace Webly.Services.UseCases.Chat;

/// <summary>
/// The site's open thread, as the editor loads it — including the run id of a turn that is still going.
///
/// That last part is what makes a reload survivable: the run outlived the socket, so the page has to be able to find
/// it again and resubscribe from sequence zero. Without it the person sees a thread that ends mid-sentence and an
/// answer that never arrives, even though the turn is still running and about to change their site.
/// </summary>
public class GetChat(
    ISiteRepository siteRepository,
    IConversationRepository conversations,
    RunRegistry registry)
{
    private const int LoadMessages = 100;

    public async Task<Result<SiteError, ConversationResponse>> ExecuteAsync(
        int userId,
        string siteNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError, ConversationResponse>.Fail(SiteError.NotFound);

        var conversation = await conversations.FindActiveAsync(site.Id, cancellationToken);

        // An empty thread rather than a 404: "you have not said anything yet" is a normal state of a chat, and an
        // error would make the editor's first load look like a failure.
        if (conversation is null)
            return Result<SiteError, ConversationResponse>.Ok(new ConversationResponse
            {
                Nanoid = string.Empty,
                Messages = []
            });

        var messages = await conversations.ListMessagesAsync(conversation.Id, LoadMessages, cancellationToken);

        return Result<SiteError, ConversationResponse>.Ok(new ConversationResponse
        {
            Nanoid = conversation.Nanoid,
            Title = conversation.Title,
            Messages = [.. messages.Where(x => x.Role is not MessageRole.Tool).Select(ChatMapper.ToResponse)],
            ActiveRunId = registry.FindByCorrelation(RunKind.Chat, conversation.Nanoid)?.RunId
        });
    }
}
