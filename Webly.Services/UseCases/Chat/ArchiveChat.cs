using Webly.Data;
using Webly.Data.Models.Chat;
using Webly.Data.Repositories.Chat;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;

namespace Webly.Services.UseCases.Chat;

/// <summary>
/// "New chat": archives the open thread so the next message starts a fresh one.
///
/// Archived, not deleted. The versions that thread produced point at its messages, and that link is what lets the
/// history show the sentence that asked for each change — deleting the thread would leave a site's whole past mute.
/// </summary>
public class ArchiveChat(
    ISiteRepository siteRepository,
    IConversationRepository conversations,
    WeblyDbContext dbContext)
{
    public async Task<Result<SiteError>> ExecuteAsync(
        int userId,
        string siteNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError>.Fail(SiteError.NotFound);

        var conversation = await conversations.FindActiveAsync(site.Id, cancellationToken);

        // Idempotent: archiving when there is nothing open is what a double click looks like, not an error.
        if (conversation is null) return Result<SiteError>.Ok();

        conversation.Status = ConversationStatus.Archived;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<SiteError>.Ok();
    }
}
