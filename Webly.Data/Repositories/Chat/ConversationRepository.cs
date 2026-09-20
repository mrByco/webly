using Microsoft.EntityFrameworkCore;
using Webly.Data.Models.Chat;

namespace Webly.Data.Repositories.Chat;

public class ConversationRepository(WeblyDbContext dbContext) : IConversationRepository
{
    public Task<Conversation?> FindActiveAsync(int siteId, CancellationToken cancellationToken = default) =>
        dbContext.Conversations
            .FirstOrDefaultAsync(
                x => x.SiteId == siteId && x.Status == ConversationStatus.Active,
                cancellationToken);

    public Task<Conversation?> FindAsync(string nanoid, int siteId, CancellationToken cancellationToken = default) =>
        dbContext.Conversations
            .FirstOrDefaultAsync(x => x.Nanoid == nanoid && x.SiteId == siteId, cancellationToken);

    public async Task<List<ConversationMessage>> ListMessagesAsync(
        int conversationId,
        int take,
        CancellationToken cancellationToken = default)
    {
        // Newest N by sequence, then reversed: the model wants the most recent history, in order.
        var newest = await dbContext.ConversationMessages
            .Where(x => x.ConversationId == conversationId)
            .OrderByDescending(x => x.Sequence)
            .Take(take)
            .ToListAsync(cancellationToken);

        newest.Reverse();
        return newest;
    }

    public async Task<int> NextSequenceAsync(int conversationId, CancellationToken cancellationToken = default)
    {
        var last = await dbContext.ConversationMessages
            .Where(x => x.ConversationId == conversationId)
            .MaxAsync(x => (int?)x.Sequence, cancellationToken);

        return (last ?? 0) + 1;
    }

    public void Add(Conversation conversation) => dbContext.Conversations.Add(conversation);

    public void AddMessage(ConversationMessage message) => dbContext.ConversationMessages.Add(message);
}
