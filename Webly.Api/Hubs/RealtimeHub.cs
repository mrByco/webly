using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;
using Webly.Api.Extensions;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Chat;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services.Authentication;
using Webly.Services.Services.Realtime;

namespace Webly.Api.Hubs;

/// <summary>What the server pushes. One method, so adding an event never adds a hub method.</summary>
public interface IRealtimeClient
{
    Task RunEvent(RunEventEnvelope envelope);
}

/// <summary>Where a subscription's replay ended, so the client knows its resume point.</summary>
public record RunSubscription(RunKind RunKind, string RunId, long LastSeq, bool IsLive);

/// <summary>
/// One multiplexed connection carrying agent turns and deployments.
///
/// SignalR rather than a raw WebSocket for three things that would otherwise be most of the work: groups (one per
/// run), automatic reconnect, and a backplane if this ever runs on more than one instance. And a socket rather than
/// streaming over POST because the product needs the <i>other</i> direction too: Stop, which has to reach a run that
/// no longer has a request. See docs/agent-plan.md.
///
/// <b>The ordering inside <see cref="Subscribe"/> is load-bearing:</b> join the group first, then replay. The other
/// order drops anything emitted between the replay and the join, which is precisely the reconnect case. The resulting
/// overlap is harmless because every envelope carries a <c>Seq</c> and the client discards what it has seen.
/// </summary>
[Authorize]
public class RealtimeHub(
    RunRegistry registry,
    IChatRunLauncher launcher,
    AgentBudget budget,
    ISiteRepository siteRepository,
    IAccessTokenBlacklist blacklist,
    ILogger<RealtimeHub> logger) : Hub<IRealtimeClient>
{
    /// <summary>A run's group name. The publisher uses the same method, so the two cannot drift.</summary>
    public static string GroupFor(RunKind kind, string runId) => $"{kind.ToString().ToLowerInvariant()}:{runId}";

    /// <summary>
    /// The caller, checked on **every** invocation rather than at connect time.
    ///
    /// A hub's <c>ClaimsPrincipal</c> is captured during the handshake and never looked at again, which is a
    /// longer-lived credential than anything else in this product: signing out revoked the session everywhere
    /// except on the socket the page had already opened. Demonstrated rather than reasoned about — connect,
    /// log out over HTTP until <c>/api/sites</c> answers 401, then invoke <c>StartChat</c> on the connection
    /// that is still open, and a real turn started as the signed-out user, spending their budget on their site.
    ///
    /// So the blacklist that ends an HTTP session immediately is asked here too. It is an <c>IMemoryCache</c>
    /// lookup, which is why it can be on the path of every invocation rather than only the expensive ones.
    ///
    /// The client closes its connection on sign-out as well, and that is the half that fixes the ordinary case.
    /// This is the half that does not depend on the client doing anything. **Neither closes the window
    /// completely**: the blacklist holds the <c>jti</c> presented at logout, so a socket that connected with a
    /// token the cookie middleware has since rotated away carries a different id and is not recognised. Closing
    /// that needs a session identity in the token rather than a per-token one — see `whats_next.md`.
    /// </summary>
    private int CallerId()
    {
        var userId = Context.User.GetUserIdVerified();
        var tokenId = Context.User?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var verified = Context.User?.IsEmailVerified() ?? false;

        if (tokenId is not null && blacklist.IsRevoked(tokenId, userId, verified))
            throw new HubException("You are not signed in.");

        return userId;
    }

    /// <summary>
    /// Starts an agent turn and returns its run id. Deliberately two steps — start, then
    /// <see cref="Subscribe"/> — so that a client which reconnects mid-run resubscribes to a run it did not start on
    /// this connection by exactly the same path.
    /// </summary>
    public async Task<RunStarted> StartChat(SendMessageRequest request)
    {
        var userId = CallerId();

        if (string.IsNullOrWhiteSpace(request.Message))
            throw new HubException("A message cannot be empty.");

        // Authorized here, on the way in, and never again inside the run: the run has no HttpContext and no
        // ClaimsPrincipal, so this is the one moment the scope is decided.
        var site = await siteRepository.FindForOwnerLightAsync(request.SiteNanoid, userId, Context.ConnectionAborted)
            ?? throw new HubException("That site could not be found.");

        // Checked here rather than with an [EnableRateLimiting] attribute, which would not apply to a hub invocation
        // at all — see AgentBudget.
        if (!budget.TryStartTurn(userId))
            throw new HubException("You have made a lot of changes in the last hour. Try again shortly.");

        logger.LogInformation("User {User} started a turn on site {Site}.", userId, site.Nanoid);

        return launcher.Start(request, userId);
    }

    /// <summary>
    /// Joins a run's group and replays everything after <paramref name="fromSeq"/> to this caller only. A client that
    /// dropped at sequence N resubscribes with N and sees no gap and no duplicates.
    /// </summary>
    public async Task<RunSubscription> Subscribe(RunKind runKind, string runId, long fromSeq = 0)
    {
        var userId = CallerId();
        var handle = registry.Get(runId);

        // A run that is not in the registry is either finished and evicted or somebody else's, and both answer the
        // same way: an unknown run. Whether a stranger's run exists is not something to confirm.
        if (handle is null || handle.UserId != userId)
            throw new HubException("That run could not be found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(runKind, runId));
        handle.AddSubscriber();
        Track(runKind, runId);

        var lastSeq = fromSeq;

        foreach (var envelope in handle.Replay(fromSeq))
        {
            await Clients.Caller.RunEvent(envelope);
            lastSeq = Math.Max(lastSeq, envelope.Seq);
        }

        return new RunSubscription(runKind, runId, lastSeq, handle.FinishedAt is null);
    }

    public async Task Unsubscribe(RunKind runKind, string runId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(runKind, runId));
        registry.Get(runId)?.RemoveSubscriber();
        Subscriptions().Remove((runKind, runId));
    }

    /// <summary>The only thing that stops a run. A dropped connection deliberately does not.</summary>
    public bool Cancel(string runId) => registry.TryCancel(runId, CallerId());

    /// <summary>
    /// The live run for a conversation or a deployment, if there is one. What a reloading editor calls to re-attach to
    /// a turn it has lost the run id of.
    /// </summary>
    public string? FindRun(RunKind runKind, string correlationId)
    {
        var handle = registry.FindByCorrelation(runKind, correlationId);

        return handle?.UserId == CallerId() ? handle.RunId : null;
    }

    /// <summary>
    /// Subscriber counts have to come back down when a connection drops, not just when a client politely unsubscribes —
    /// the orphan reaper's whole job depends on the count being true, and a browser that was closed sends nothing.
    /// </summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var (_, runId) in Subscriptions())
            registry.Get(runId)?.RemoveSubscriber();

        return base.OnDisconnectedAsync(exception);
    }

    private void Track(RunKind kind, string runId) => Subscriptions().Add((kind, runId));

    private HashSet<(RunKind Kind, string RunId)> Subscriptions()
    {
        if (Context.Items.TryGetValue(SubscriptionsKey, out var existing)
            && existing is HashSet<(RunKind, string)> subscriptions)
            return subscriptions;

        var created = new HashSet<(RunKind, string)>();
        Context.Items[SubscriptionsKey] = created;

        return created;
    }

    private const string SubscriptionsKey = "webly:subscriptions";
}
