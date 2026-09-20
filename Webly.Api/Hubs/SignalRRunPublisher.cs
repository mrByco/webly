using Microsoft.AspNetCore.SignalR;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services.Realtime;

namespace Webly.Api.Hubs;

/// <summary>
/// Publishes to the run's SignalR group. The services assembly knows events are published; only this class knows they
/// travel over a hub — which is what keeps <c>Webly.Services</c> free of a transport dependency and testable without
/// one.
/// </summary>
public class SignalRRunPublisher(IHubContext<RealtimeHub, IRealtimeClient> hub) : IRunPublisher
{
    public Task PublishAsync(RunEventEnvelope envelope, CancellationToken cancellationToken = default) =>
        hub.Clients
            .Group(RealtimeHub.GroupFor(envelope.RunKind, envelope.RunId))
            .RunEvent(envelope);
}
