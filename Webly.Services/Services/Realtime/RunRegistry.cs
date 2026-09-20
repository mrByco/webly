using System.Collections.Concurrent;
using Webly.Services.DTO.Realtime;

namespace Webly.Services.Services.Realtime;

/// <summary>
/// A run executing on this instance.
///
/// <b>Run lifetime lives here, not on an <c>HttpContext</c></b>, and that is the point of the whole class: a
/// closed tab must not cancel an agent turn that is halfway through rewriting somebody's home page. Only an
/// explicit Cancel stops a run, plus <see cref="OrphanRunReaper"/> for the case where nobody comes back.
///
/// The event log is here too, in memory, rather than in a table — see <see cref="RunKind.Chat"/>. It exists
/// only to serve a reconnect, and a chat run cannot outlive the process that is running it, so a row per
/// token would be a migration, a sweep and a serializer path in exchange for durability nothing can use.
/// <see cref="IRunEventSink"/> is the seam if that ever changes.
/// </summary>
public sealed class RunHandle
{
    public required RunKind Kind { get; init; }
    public required string RunId { get; init; }

    /// <summary>
    /// The conversation nanoid for a chat run, the deployment nanoid for a deploy. Lets a reloading editor ask
    /// "is something already running for this site?" without having kept the run id.
    ///
    /// Settable because a turn that starts a <i>new</i> conversation only learns its nanoid a moment after the
    /// run is registered — and a client that reloads in that moment must still be able to re-attach.
    /// </summary>
    public string? CorrelationId { get; internal set; }

    public int UserId { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    internal CancellationTokenSource Cts { get; init; } = new();
    public CancellationToken Token => Cts.Token;

    private readonly List<RunEventEnvelope> _log = [];
    private long _seq;

    /// <summary>Appends an event and gives it its sequence number. The sink calls this before publishing.</summary>
    internal RunEventEnvelope Append(RunEvent runEvent, bool isTerminal)
    {
        lock (_log)
        {
            var envelope = new RunEventEnvelope(Kind, RunId, ++_seq, runEvent, isTerminal);
            _log.Add(envelope);

            return envelope;
        }
    }

    /// <summary>Everything after <paramref name="fromSeq"/>, for a subscriber that arrived late.</summary>
    public IReadOnlyList<RunEventEnvelope> Replay(long fromSeq)
    {
        lock (_log)
        {
            return [.. _log.Where(x => x.Seq > fromSeq)];
        }
    }

    private int _subscribers;
    public int SubscriberCount => Volatile.Read(ref _subscribers);

    /// <summary>
    /// When the run last had nobody watching. Seeded to now so a client that starts a run and immediately dies
    /// is reaped on the same clock as one that walks away later — otherwise "never subscribed" would look like
    /// "always watched".
    /// </summary>
    public DateTime UnwatchedSince { get; private set; } = DateTime.UtcNow;

    internal void AddSubscriber()
    {
        Interlocked.Increment(ref _subscribers);
        UnwatchedSince = DateTime.MaxValue;
    }

    internal void RemoveSubscriber()
    {
        if (Interlocked.Decrement(ref _subscribers) <= 0)
        {
            Interlocked.Exchange(ref _subscribers, 0);
            UnwatchedSince = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Questions the agent is waiting on answers to, by question id. On the handle rather than in a service,
    /// because a pending question belongs to exactly one run and dies with it — see <c>QuestionToolkit</c>.
    /// </summary>
    internal ConcurrentDictionary<string, TaskCompletionSource<string>> PendingQuestions { get; } = new();

    /// <summary>When the run finished, if it has. A completed handle lingers so that a client which reconnects
    /// a few seconds late still gets the whole story instead of "no such run".</summary>
    public DateTime? FinishedAt { get; internal set; }
}

/// <summary>
/// The live runs, keyed by run id, with cancellation decoupled from any connection.
///
/// In-process by design: a run executes on one instance, so the instance that owns it is the one that can
/// cancel it. Scaling out needs a cancel message on the SignalR backplane, not a different registry.
/// </summary>
public sealed class RunRegistry
{
    private readonly ConcurrentDictionary<string, RunHandle> _runs = new();

    public RunHandle Register(RunKind kind, string runId, string? correlationId, int userId, CancellationToken linkedTo = default)
    {
        var handle = new RunHandle
        {
            Kind = kind,
            RunId = runId,
            CorrelationId = correlationId,
            UserId = userId,
            Cts = CancellationTokenSource.CreateLinkedTokenSource(linkedTo)
        };

        _runs[runId] = handle;

        return handle;
    }

    public RunHandle? Get(string runId) => _runs.TryGetValue(runId, out var handle) ? handle : null;

    /// <summary>The run for a conversation or deployment, if one is live. Used by a reloading client to
    /// re-attach without having kept the run id.</summary>
    public RunHandle? FindByCorrelation(RunKind kind, string correlationId) =>
        _runs.Values.FirstOrDefault(x => x.Kind == kind && x.CorrelationId == correlationId && x.FinishedAt is null);

    public IReadOnlyList<RunHandle> All => [.. _runs.Values];

    /// <summary>
    /// Marks a run finished but keeps the handle, so a late subscriber can still replay it. The reaper evicts
    /// it a few minutes later — <see cref="Evict"/>.
    /// </summary>
    public void Finish(string runId)
    {
        if (_runs.TryGetValue(runId, out var handle))
            handle.FinishedAt = DateTime.UtcNow;
    }

    public void Evict(string runId)
    {
        if (_runs.TryRemove(runId, out var handle))
            handle.Cts.Dispose();
    }

    /// <summary>
    /// Explicit cancellation — the person pressed Stop. False when the run is unknown (already finished) or
    /// belongs to somebody else, which is the same answer on purpose: whether a stranger's run exists is not
    /// something to confirm.
    /// </summary>
    public bool TryCancel(string runId, int byUserId)
    {
        if (!_runs.TryGetValue(runId, out var handle)) return false;
        if (handle.UserId != byUserId) return false;

        try
        {
            handle.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run finished between the lookup and the cancel. Nothing to do, and not an error.
        }

        return true;
    }

    /// <summary>
    /// Routes an answer to the question a run is waiting on. Returns false for an unknown run, an unknown
    /// question, or somebody else's — again, one answer for all three.
    /// </summary>
    public bool TryAnswer(string runId, string questionId, string answer, int byUserId)
    {
        if (!_runs.TryGetValue(runId, out var handle)) return false;
        if (handle.UserId != byUserId) return false;

        return handle.PendingQuestions.TryRemove(questionId, out var pending)
            && pending.TrySetResult(answer);
    }
}
