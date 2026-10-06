using Webly.Data.Repositories.Chat;
using Webly.Services.DTO.Realtime;

namespace Webly.Services.Services.Realtime;

/// <summary>
/// The turn editing a site right now, if there is one.
///
/// For the operations that change a site from outside a turn — bringing a version back, writing a new name into
/// the pages, adding or removing a photograph — and that therefore <b>refuse while one runs</b>. Each of them
/// commits to the branch the running turn is about to commit to, then re-seeds the workspace that turn is using,
/// and both halves went wrong together: the re-seed waits on the turn's gate, so the request hung for the rest of
/// the turn; and the turn's own commit then found the head moved and was refused, so the work somebody had just
/// paid a model for was thrown away with "Your site changed while this was being saved". Driven in the running app
/// with the mock agent taking its time: a restore pressed eight seconds in answered fourteen seconds later, and the
/// turn failed. A refusal is immediate and loses nothing, and Stop is one button away for somebody who means it.
///
/// Not a lock: a turn can still start in the moment between this answering and the operation committing. That
/// window is the length of one git commit rather than of a turn, and what it costs is what the rule above prevents
/// in the ordinary case — the turn's commit is refused cleanly and says so.
/// </summary>
public class SiteTurns(
    IConversationRepository conversations,
    RunRegistry runs)
{
    public async Task<RunHandle?> FindRunningAsync(int siteId, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.FindActiveAsync(siteId, cancellationToken);

        return conversation is null ? null : runs.FindByCorrelation(RunKind.Chat, conversation.Nanoid);
    }

    public async Task<bool> IsRunningAsync(int siteId, CancellationToken cancellationToken = default) =>
        await FindRunningAsync(siteId, cancellationToken) is not null;
}
