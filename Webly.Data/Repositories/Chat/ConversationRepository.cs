using Microsoft.EntityFrameworkCore;
using Npgsql;
using Webly.Data.Models.Chat;

namespace Webly.Data.Repositories.Chat;

public class ConversationRepository(WeblyDbContext dbContext) : IConversationRepository
{
    public Task<Conversation?> FindActiveAsync(int siteId, CancellationToken cancellationToken = default) =>
        dbContext.Conversations
            .FirstOrDefaultAsync(
                x => x.SiteId == siteId && x.Status == ConversationStatus.Active,
                cancellationToken);

    public async Task<Conversation> FindOrCreateActiveAsync(
        int siteId,
        int userId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindActiveAsync(siteId, cancellationToken);

        if (existing is not null) return existing;

        var conversation = new Conversation { SiteId = siteId, UserId = userId, Title = title };

        dbContext.Conversations.Add(conversation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return conversation;
        }
        catch (DbUpdateException exception) when (IsOneActivePerSiteViolation(exception))
        {
            // Somebody else's turn created it in the milliseconds since the read above. Detached first,
            // because the context is still tracking a row the database refused — leaving it there would make
            // every later save in this turn try to insert it again.
            dbContext.Entry(conversation).State = EntityState.Detached;

            return await FindActiveAsync(siteId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Site {siteId} has no active conversation, and inserting one was refused.", exception);
        }
    }

    /// <summary>
    /// Whether this is the one violation worth retrying: the partial unique index that says a site has at most
    /// one open thread. Matched on the constraint's own name rather than on <c>23505</c> alone, because
    /// swallowing every unique violation here would hide a real bug the first time another index is added.
    /// </summary>
    private static bool IsOneActivePerSiteViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: "23505" } postgres
        && postgres.ConstraintName == "IX_Conversations_OneActivePerSite";

    public Task<Conversation?> FindAsync(string nanoid, int siteId, CancellationToken cancellationToken = default) =>
        dbContext.Conversations
            .FirstOrDefaultAsync(x => x.Nanoid == nanoid && x.SiteId == siteId, cancellationToken);

    public async Task<List<ConversationMessage>> ListMessagesAsync(
        int conversationId,
        int take,
        CancellationToken cancellationToken = default)
    {
        // Newest N by sequence, then reversed: the model wants the most recent history, in order.
        //
        // The produced version is included because `ChatMessageResponse.ProducedVersionNanoid` promises it, and
        // without the include it was null on every message — the link that makes the history read as this
        // conversation existed in the database and in the DTO and nowhere in between. A left join on an indexed
        // foreign key for at most `take` rows; the agent's own call does not need it and does not notice it.
        var newest = await dbContext.ConversationMessages
            .Where(x => x.ConversationId == conversationId)
            .Include(x => x.ProducedVersion)
            .OrderByDescending(x => x.Sequence)
            .Take(take)
            .ToListAsync(cancellationToken);

        newest.Reverse();
        return newest;
    }

    /// <summary>
    /// Namespaces this repository's advisory locks, so that a lock on conversation 7 cannot collide with some
    /// other part of the system locking on the number 7. Arbitrary and constant; only its uniqueness matters.
    /// </summary>
    private const int MessageLockClass = 8_141;

    public async Task AppendMessageAsync(
        ConversationMessage message,
        CancellationToken cancellationToken = default)
    {
        // A Postgres advisory lock on this one conversation, held until the transaction ends. It serializes the
        // two statements that have to be one — read the highest sequence, insert the next — for writers in any
        // process, which is what the alternative could not do: retrying against the unique index works for two
        // writers and degrades for ten, because every retry re-reads the same number the others just read.
        // A transaction-scoped lock also cannot be leaked; it goes when this commits or rolls back.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({MessageLockClass}, {message.ConversationId})", cancellationToken);

        message.Sequence = await NextSequenceAsync(message.ConversationId, cancellationToken);

        dbContext.ConversationMessages.Add(message);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<int> NextSequenceAsync(int conversationId, CancellationToken cancellationToken)
    {
        var last = await dbContext.ConversationMessages
            .Where(x => x.ConversationId == conversationId)
            .MaxAsync(x => (int?)x.Sequence, cancellationToken);

        return (last ?? 0) + 1;
    }

    public void Add(Conversation conversation) => dbContext.Conversations.Add(conversation);

}
