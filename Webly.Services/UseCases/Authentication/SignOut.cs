using Webly.Data.Repositories.RefreshTokens;
using Webly.Services.Services.Authentication;

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
/// </summary>
public class SignOut(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IAccessTokenBlacklist accessTokenBlacklist)
{
    public async Task Execute(
        int userId,
        string? rawRefreshToken,
        string? accessTokenId = null,
        DateTimeOffset? accessTokenExpiresAt = null,
        CancellationToken cancellationToken = default)
    {
        if (accessTokenId is not null && accessTokenExpiresAt is not null)
            accessTokenBlacklist.Revoke(accessTokenId, accessTokenExpiresAt.Value);

        if (string.IsNullOrEmpty(rawRefreshToken))
        {
            // No refresh cookie to identify which session this is, so end all of them rather than
            // leaving the user unable to log out at all.
            await refreshTokenRepository.RevokeAllForUserAsync(userId, DateTime.UtcNow, cancellationToken);
            return;
        }

        var hash = tokenService.HashRefreshToken(rawRefreshToken);
        var token = await refreshTokenRepository.FindByHashAsync(hash, cancellationToken);

        // Only honour a token that belongs to the caller, so one user cannot end another's session
        // by presenting a token they scraped from somewhere.
        if (token is null || token.UserId != userId)
            return;

        await refreshTokenRepository.RevokeAsync(token, DateTime.UtcNow, cancellationToken);
    }
}
