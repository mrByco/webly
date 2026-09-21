using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Webly.Services.Services.Realtime;

/// <inheritdoc cref="IRealtimeSessions"/>
public class RealtimeSessions(ILogger<RealtimeSessions> logger) : IRealtimeSessions
{
    /// <summary>
    /// One entry per live connection, keyed by an id this class mints rather than by the connection's own: the
    /// caller hands over a callback and gets back the only thing that removes it, so nothing outside can forget
    /// somebody else's socket.
    /// </summary>
    private readonly ConcurrentDictionary<long, (int UserId, string? SessionId, Action Abort)> _connections = new();

    private long _next;

    public IDisposable Register(int userId, string? sessionId, Action abort)
    {
        var key = Interlocked.Increment(ref _next);

        _connections[key] = (userId, sessionId, abort);

        return new Registration(() => _connections.TryRemove(key, out _));
    }

    public void End(int userId, string? sessionId)
    {
        // A connection with no session id is one whose token was minted before the claim existed. It cannot be
        // matched and is therefore ended whenever its user signs out anywhere — see the interface.
        var ending = _connections
            .Where(x => x.Value.UserId == userId && (sessionId is null || x.Value.SessionId is null || x.Value.SessionId == sessionId))
            .ToList();

        if (ending.Count == 0) return;

        logger.LogInformation(
            "Ending {Count} realtime connection(s) for user {User}, session {Session}.",
            ending.Count, userId, sessionId ?? "(all)");

        foreach (var (key, entry) in ending)
        {
            // Removed first, so a disconnect callback racing with this finds nothing rather than aborting twice.
            _connections.TryRemove(key, out _);

            try
            {
                entry.Abort();
            }
            catch (Exception exception)
            {
                // A connection that is already gone throws here, and a sign-out that failed because a socket
                // was closing would be the worse outcome by a distance.
                logger.LogDebug(exception, "Could not end a realtime connection for user {User}.", userId);
            }
        }
    }

    private sealed class Registration(Action forget) : IDisposable
    {
        public void Dispose() => forget();
    }
}
