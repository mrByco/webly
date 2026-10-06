namespace Webly.Services.Services.Realtime;

/// <summary>
/// Why a run was cancelled, kept on its <see cref="RunHandle"/> so the sentence the thread ends on can be true.
///
/// All three of these, and a shutdown, used to end a turn with "Stopped. Nothing was changed." — which reads as the
/// person having pressed Stop. A deploy of Webly sends the process a polite shutdown, the run's token is linked to
/// it, and every turn in flight was recorded as stopped by somebody who had done nothing of the kind. A shutdown has
/// no value here on purpose: it reaches the run through the linked token rather than through anybody calling
/// <see cref="RunRegistry.TryCancel"/>, and "nobody gave a reason" is exactly what tells it apart.
/// </summary>
public enum CancelReason
{
    /// <summary>The person pressed Stop.</summary>
    Stopped,

    /// <summary>Nobody watched it for long enough that <see cref="OrphanRunReaper"/> ended it.</summary>
    Unwatched,

    /// <summary>It ran past the reaper's lifetime cap.</summary>
    TooLong
}
