using Webly.Data.Models.Authentication;
using Webly.Services.DTO.Authentication;

namespace Webly.Services.Services.Authentication;

public interface IAuthSessionService
{
    /// <summary>
    /// Starts a session for a user: mints an access token, stores a fresh refresh token, and
    /// returns both alongside the profile. Every sign-in path ends here, so a session created by
    /// registration, by password, by Google or by rotation is the same session in every respect.
    /// </summary>
    /// <param name="continuingSessionId">
    /// The session these new tokens continue, or null to begin one. A rotation continues — the tokens change and
    /// the session does not, which is the whole point of <see cref="RefreshToken.SessionId"/> — and everything
    /// else here is somebody signing in.
    /// </param>
    Task<AuthResult> IssueAsync(
        User user,
        string? continuingSessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every session a user has — their refresh tokens, and everything those sessions already hold: the access
    /// tokens and preview cookies they minted, through the blacklist, and their open hub connections.
    ///
    /// <b>Revoking the refresh tokens was all that used to happen</b>, at five call sites that each meant "nobody is
    /// signed in as this person any more": a password reset, a password change, a replayed refresh token, an
    /// unverified account taken over by the address's real owner, and a sign-out with no cookie to name its session.
    /// Everything already minted lived on — an access token for its fifteen minutes, a preview cookie for twelve
    /// hours, and an open hub socket, which is how a turn is started, for as long as it stayed open. Found by
    /// resetting a password with a second browser signed in, which went on answering 200.
    /// </summary>
    Task EndAllAsync(int userId, CancellationToken cancellationToken = default);

    MeResponse Describe(User user);
}
