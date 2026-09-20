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

    /// <summary>The next <see cref="ConversationMessage.Sequence"/>, so the writer never guesses.</summary>
    Task<int> NextSequenceAsync(int conversationId, CancellationToken cancellationToken = default);

    void Add(Conversation conversation);

    void AddMessage(ConversationMessage message);
}
