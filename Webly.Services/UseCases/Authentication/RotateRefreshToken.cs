using Webly.Data;
using Webly.Data.Repositories.RefreshTokens;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;

namespace Webly.Services.UseCases.Authentication;

/// <summary>
/// Exchanges a refresh token for a new session, revoking the one presented.
///
/// Rotation makes a refresh token single-use, which in turn makes theft detectable: if a token that
/// has already been rotated shows up again, either the browser's copy or a stolen one is being
/// replayed, and there is no way to tell which. Both are logged out — the reference project instead
/// ignored its own revocation flag on lookup, which quietly made revoking a token do nothing.
///
/// <b><see cref="ReuseGrace"/> is what stops that being a bug rather than a defence</b>, and it was one:
/// a browser sends several requests at once, and when the access token dies they <i>all</i> arrive carrying
/// the same live refresh cookie. One rotates; every other one is a replay of a token revoked milliseconds
/// ago, so the session was destroyed — reliably, every fifteen minutes, for any page that loads more than
/// one thing. Inside the grace window a replay is therefore read as what it almost always is: the rest of a
/// batch. Outside it, nothing changes.
/// </summary>
public class RotateRefreshToken(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IAuthSessionService authSessionService,
    WeblyDbContext dbContext)
{
    /// <summary>
    /// How long after a rotation its predecessor is still accepted. Seconds, because the only thing it has to
    /// cover is one page's worth of requests in flight together — long enough for a slow first paint, far too
    /// short to be worth stealing a cookie for, and the same compromise every rotating-refresh-token
    /// implementation makes.
    /// </summary>
    public static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(30);

    public async Task<AuthResult> Execute(string rawToken, CancellationToken cancellationToken = default)
    {
        var hash = tokenService.HashRefreshToken(rawToken);
        var existing = await refreshTokenRepository.FindByHashAsync(hash, cancellationToken);

        if (existing is null)
            return AuthResult.Fail(AuthError.InvalidCredentials);

        var now = DateTime.UtcNow;

        if (existing.RevokedAt is not null)
        {
            // Replay of a spent token. Drop every live session of this user rather than only
            // refusing this one request — whoever holds the leaked cookie also holds its successor.
            //
            // `ReplacedAt`, not `RevokedAt`: the grace window belongs to a token that was *rotated*, and a
            // token revoked by signing out has to stop working immediately. Reading the wrong one of these
            // makes logout last thirty seconds longer than the person asked for.
            if (existing.ReplacedAt is not { } replacedAt || now - replacedAt > ReuseGrace)
            {
                await refreshTokenRepository.RevokeAllForUserAsync(existing.UserId, now, cancellationToken);
                return AuthResult.Fail(AuthError.InvalidCredentials);
            }

            // Inside the window: one of a batch, whose sibling rotated a moment ago. An access token and no
            // new refresh token — the successor already exists and the caller leaves that cookie alone, so
            // whichever response reaches the browser last, it is holding a live one. Minting another here
            // instead would leave a spare live token per parallel request, for ever.
            return AuthResult.SuccessWithoutRotation(
                tokenService.CreateAccessToken(existing.User, existing.SessionId),
                authSessionService.Describe(existing.User));
        }

        if (existing.ExpiresAt <= now)
            return AuthResult.Fail(AuthError.InvalidCredentials);

        existing.RevokedAt = now;
        existing.ReplacedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        // The same session, with new tokens: rotation is what a session does to stay alive, not a new one
        // beginning. Passing null here would mint a fresh session id every fifteen minutes and make the id
        // exactly as short-lived as the `jti` it exists to outlive.
        return await authSessionService.IssueAsync(existing.User, existing.SessionId, cancellationToken);
    }
}
