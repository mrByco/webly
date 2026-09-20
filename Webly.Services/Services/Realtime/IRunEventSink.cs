using Webly.Services.DTO.Realtime;

namespace Webly.Services.Services.Realtime;

/// <summary>
/// Where every event goes. Two steps, in this order and never the other: append to the run's log, then
/// publish. A publish-first sink loses any event that arrives between a late subscriber's replay query and its
/// group join — which is the reconnect case, i.e. exactly the case the log exists for.
/// </summary>
public interface IRunEventSink
{
    Task EmitAsync(string runId, RunEvent runEvent, bool isTerminal = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// How an event leaves the process. Implemented in the API layer over SignalR — the services assembly knows
/// that events are published, not that they travel over a hub.
/// </summary>
public interface IRunPublisher
{
    Task PublishAsync(RunEventEnvelope envelope, CancellationToken cancellationToken = default);
}

public class RunEventSink(RunRegistry registry, IRunPublisher publisher) : IRunEventSink
{
    public async Task EmitAsync(
        string runId,
        RunEvent runEvent,
        bool isTerminal = false,
        CancellationToken cancellationToken = default)
    {
        var handle = registry.Get(runId);

        // A run that has left the registry can still have an event in flight (a cancelled tool finishing its
        // sentence). Publishing it anyway is harmless — a client filters on Seq — but there is nowhere to
        // append it, so it cannot be replayed. Dropping it silently would be worse than a gap nobody sees.
        if (handle is null) return;

        var envelope = handle.Append(runEvent, isTerminal);

        await publisher.PublishAsync(envelope, cancellationToken);
    }
}
