using Webly.Data.Repositories.RefreshTokens;
using Webly.Services.Services.Authentication;
using Webly.Services.Services.Realtime;

namespace Webly.Services.UseCases.Authentication;

/// <summary>
/// Ends the caller's session server-side. Clearing the cookies is the caller's job; this is what
/// makes the logout real, because a cookie the browser has already handed to someone else cannot be
/// recalled.
///
/// Both halves of the session are ended: the refresh token is revoked in the database, and the
/// access token — which is signed and would otherwise stay valid until it expired — is blacklisted.
/// The reference project did neither, and only deleted the cookies.
///
/// This ends <i>this</i> session, not every session the user has. Logging out on one device should
/// not sign you out on the others.
///
/// <b>The third half is the realtime connection</b>, which is not a cookie and not a token and was therefore
/// missed entirely: a hub reads its caller's identity once, during the handshake, so a socket the browser had
/// already opened went on being that person's after this ran — a turn could still be started over it. The
/// session the presented refresh token belongs to is what names the sockets to end, which is why
/// <see cref="Webly.Data.Models.Authentication.RefreshToken.SessionId"/> exists at all.
/// </summary>
public class SignOut(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IAccessTokenBlacklist accessTokenBlacklist,
    IRealtimeSessions realtimeSessions)
{
    /// <summary>
    /// How long a revoked session is remembered: the longest any credential minted under it can outlive the
    /// sign-out. The preview token's twelve hours is that credential today, and the access token's fifteen
    /// minutes is caught by its own id anyway. A day rather than the refresh token's sixty, because the
    /// entries are held in memory and remembering a session long after everything it could have minted has
    /// expired is paying for nothing.
    /// </summary>
    private static readonly TimeSpan SessionRevocationWindow = TimeSpan.FromDays(1);

    public async Task Execute(
        int userId,
        string? rawRefreshToken,
        string? accessTokenId = null,
        DateTimeOffset? accessTokenExpiresAt = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (accessTokenId is not null && accessTokenExpiresAt is not null)
            accessTokenBlacklist.Revoke(accessTokenId, accessTokenExpiresAt.Value);

        // The caller's own session, whether or not a refresh cookie came with the request — an access token is
        // enough to say which session is signing out, and everything minted under it goes with it.
        if (!string.IsNullOrEmpty(sessionId))
            accessTokenBlacklist.RevokeSession(sessionId, DateTimeOffset.UtcNow.Add(SessionRevocationWindow));

        if (string.IsNullOrEmpty(rawRefreshToken))
        {
            // No refresh cookie to identify which session this is, so end all of them rather than
            // leaving the user unable to log out at all. The sockets follow the same rule, for the same reason.
            await refreshTokenRepository.RevokeAllForUserAsync(userId, DateTime.UtcNow, cancellationToken);
            realtimeSessions.End(userId, sessionId: null);

            return;
        }

        var hash = tokenService.HashRefreshToken(rawRefreshToken);
        var token = await refreshTokenRepository.FindByHashAsync(hash, cancellationToken);

        // Only honour a token that belongs to the caller, so one user cannot end another's session
        // by presenting a token they scraped from somewhere.
        if (token is null || token.UserId != userId)
            return;

        await refreshTokenRepository.RevokeAsync(token, DateTime.UtcNow, cancellationToken);

        // The session the cookie names as well, which is the same one in every ordinary case — and is not when
        // the access token has expired and the browser is signing out on the refresh cookie alone.
        accessTokenBlacklist.RevokeSession(token.SessionId, DateTimeOffset.UtcNow.Add(SessionRevocationWindow));

        // Last, and after the row is really revoked: a socket that outlives this call by a moment is harmless,
        // and one ended before the session was would be ended for a sign-out that then failed.
        realtimeSessions.End(userId, token.SessionId);
    }
}
