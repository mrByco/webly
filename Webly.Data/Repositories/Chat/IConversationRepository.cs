using Webly.Data.Models.Chat;

namespace Webly.Data.Repositories.Chat;

public interface IConversationRepository
{
    /// <summary>
    /// The site's open thread with its messages, or null if there is none yet. At most one exists — a
    /// partial unique index says so, not this query.
    /// </summary>
    Task<Conversation?> FindActiveAsync(int siteId, CancellationToken cancellationToken = default);

    Task<Conversation?> FindAsync(string nanoid, int siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The last <paramref name="take"/> messages of a thread, oldest first, for hydrating the model's
    /// history. Tool messages are excluded by the caller, not here — what to feed the model is the
    /// turn service's decision and not a property of the table.
    /// </summary>
    Task<List<ConversationMessage>> ListMessagesAsync(int conversationId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a message to its thread, giving it the next <see cref="ConversationMessage.Sequence"/> and
    /// saving it.
    ///
    /// <b>Assigning and saving are one operation because they race.</b> Reading the highest sequence and then
    /// inserting is a check-then-act, and two turns on one site do exactly that at the same moment — both
    /// compute the same number and the unique index on <c>(ConversationId, Sequence)</c> refuses the second,
    /// which reached the person as "something went wrong" on a message that was fine. The index is right and
    /// stays; the two statements are made one with a Postgres advisory lock on the conversation, which is
    /// deterministic where retrying against the index is not — ten writers retrying all re-read the number the
    /// others just read.
    /// </summary>
    Task AppendMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// The site's open thread, creating it if there is none — and surviving two turns doing this at the same
    /// moment, which is what a second browser tab or a double-click produces.
    ///
    /// <b>The race is real and the index catches it.</b> Both turns see no active thread, both insert, and the
    /// partial unique index refuses the second — correctly, but as a <c>DbUpdateException</c> that reached the
    /// person as "something went wrong" on a message that was perfectly fine. The thread the other turn just
    /// created is the right answer, so this returns it.
    ///
    /// It saves, which the other methods here deliberately do not: the point of it is what the database does
    /// when two writers meet, and that cannot be expressed by a caller who saves later.
    /// </summary>
    Task<Conversation> FindOrCreateActiveAsync(
        int siteId,
        int userId,
        string title,
        CancellationToken cancellationToken = default);

    void Add(Conversation conversation);
}
