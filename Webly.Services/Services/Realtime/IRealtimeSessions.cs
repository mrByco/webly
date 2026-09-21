namespace Webly.Services.Services.Realtime;

/// <summary>
/// The live realtime connections, so that ending a session can end them.
///
/// <b>A hub connection is the longest-lived credential in this product.</b> Its caller's identity is decided
/// during the handshake and never read again, so signing out revoked the session everywhere except on a socket
/// the browser had already opened — demonstrated by logging out until <c>/api/sites</c> answered 401 and then
/// starting a real turn over the connection that was still there. The blacklist closes the case where the socket
/// carries the same access token the logout presented; this closes the rest, which is any socket older than one
/// rotation of it.
///
/// <b>Per session</b>, which is what <see cref="Webly.Data.Models.Authentication.RefreshToken.SessionId"/> was
/// added for. Per *user* was the first shape and it was wrong in a way only running it showed: ending a user's
/// connections ends them on their other devices too, and <c>Context.Abort</c> closes cleanly enough that the
/// SignalR client does **not** treat it as a dropped connection — it never reconnects. Watched with two signed-in
/// clients: logging out of one left the other's socket dead and silent, which is a worse thing than the gap it
/// was closing. Naming the session means the other device is not touched at all.
///
/// Abort is passed in as a callback rather than the connection: <see cref="IRunEventSink"/> keeps SignalR out of
/// this assembly for the same reason, and one more type crossing that line for one method call is not worth it.
///
/// <b>Per process</b>, like <c>RunRegistry</c>, <c>AgentBudget</c> and the blacklist beside it: a second instance
/// holds its own sockets and would not hear this. Scaling out means a message on the backplane, and it is the
/// fifth entry on the same list.
/// </summary>
public interface IRealtimeSessions
{
    /// <summary>
    /// Records a connection and returns the handle that forgets it. Disposed when the connection drops, so a
    /// registry of dead sockets cannot accumulate — a hub always gets its disconnect callback, even for a
    /// browser that was closed.
    /// </summary>
    IDisposable Register(int userId, string? sessionId, Action abort);

    /// <summary>
    /// Ends the live connections of one session, or — when the session is not known — every connection of the
    /// user.
    ///
    /// The second case is not a convenience: a sign-out with no refresh cookie already revokes all of that
    /// user's tokens, because there is nothing to say which session it meant, and the sockets have to follow the
    /// same rule. A connection whose token predates the session claim is in the same position, and is ended for
    /// the same reason — being unable to name it is not a reason to leave it running.
    /// </summary>
    void End(int userId, string? sessionId);
}
