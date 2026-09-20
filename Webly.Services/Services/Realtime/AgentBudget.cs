using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace Webly.Services.Services.Realtime;

/// <summary>
/// How many agent turns one account may start per hour.
///
/// Its own thing rather than an <c>[EnableRateLimiting]</c> attribute, because <b>starting a turn is a hub invocation,
/// not an endpoint</b>: the ASP.NET rate-limiting middleware sits in the HTTP pipeline and never sees a message sent
/// down an established WebSocket. An attribute on the hub would look like a fence and be none — which is worse than no
/// fence at all, because somebody would believe it.
///
/// This is the money fence: every turn spends tokens against our provider keys. It complements
/// <see cref="OrphanRunReaper"/>, which caps a single runaway run rather than a flood of them.
///
/// Per process, like the access-token blacklist, and for the same reason — one instance today, and this is on the list
/// of things to move to shared storage on the day that changes. A second instance would double the budget, which is a
/// cost bug rather than a security one.
/// </summary>
public sealed class AgentBudget : IDisposable
{
    private const int TurnsPerWindow = 60;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<int, FixedWindowRateLimiter> _limiters = new();

    /// <summary>
    /// Takes one turn from this user's budget. False means they are over it, and the hub answers with a sentence rather
    /// than starting a run.
    /// </summary>
    public bool TryStartTurn(int userId)
    {
        var limiter = _limiters.GetOrAdd(userId, _ => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = TurnsPerWindow,
            Window = Window,
            QueueLimit = 0,
            AutoReplenishment = true
        }));

        using var lease = limiter.AttemptAcquire();

        return lease.IsAcquired;
    }

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
            limiter.Dispose();

        _limiters.Clear();
    }
}
