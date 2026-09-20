namespace Webly.Services.DTO.Realtime;

/// <summary>
/// The two kinds of long-running thing in Webly, and they are long-running for different reasons — which is
/// why the substrate treats them differently rather than pretending they are the same.
/// </summary>
public enum RunKind
{
    /// <summary>
    /// An agent turn. Seconds to a couple of minutes, in memory, not durable: if the process dies mid-turn
    /// the turn is gone, and that is acceptable because nothing was committed — a draft only becomes a
    /// version when the turn finishes. Its replay log lives on the <c>RunHandle</c>.
    /// </summary>
    Chat,

    /// <summary>
    /// A deployment. Durable, because it changes the outside world and must survive a restart: its state is
    /// a <c>Deployment</c> row, which is also its replay — there is no event log to keep, because "where is
    /// my publish" has exactly one answer at any moment and the row holds it.
    /// </summary>
    Deploy
}
