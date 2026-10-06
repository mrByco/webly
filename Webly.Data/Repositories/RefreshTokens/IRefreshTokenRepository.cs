using Webly.Data.Models.Authentication;

namespace Webly.Data.Repositories.RefreshTokens;

public interface IRefreshTokenRepository
{
    /// <summary>
    /// Finds a token row by its hash regardless of whether it is live, expired or revoked. The
    /// caller decides what to do with a dead one — a revoked token being presented is a signal, not
    /// simply a miss, so this must not filter it away.
    /// </summary>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    void Add(RefreshToken token);

    /// <summary>Revokes one token — ending a single session.</summary>
    Task RevokeAsync(RefreshToken token, DateTime revokedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every live token of a user and says which sessions those were, because a session holds more than its
    /// refresh token and the caller has to end the rest — see <c>IAuthSessionService.EndAllAsync</c>, the one caller.
    /// </summary>
    Task<IReadOnlyList<string>> RevokeAllForUserAsync(int userId, DateTime revokedAt, CancellationToken cancellationToken = default);
}
