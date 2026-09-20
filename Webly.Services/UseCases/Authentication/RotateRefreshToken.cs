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
/// </summary>
public class RotateRefreshToken(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IAuthSessionService authSessionService,
    WeblyDbContext dbContext)
{
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
            await refreshTokenRepository.RevokeAllForUserAsync(existing.UserId, now, cancellationToken);
            return AuthResult.Fail(AuthError.InvalidCredentials);
        }

        if (existing.ExpiresAt <= now)
            return AuthResult.Fail(AuthError.InvalidCredentials);

        existing.RevokedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await authSessionService.IssueAsync(existing.User, cancellationToken);
    }
}
