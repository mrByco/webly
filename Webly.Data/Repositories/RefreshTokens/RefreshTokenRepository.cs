using Webly.Data.Models.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Webly.Data.Repositories.RefreshTokens;

public class RefreshTokenRepository(WeblyDbContext dbContext) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        dbContext.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

    public void Add(RefreshToken token) => dbContext.RefreshTokens.Add(token);

    public Task RevokeAsync(RefreshToken token, DateTime revokedAt, CancellationToken cancellationToken = default)
    {
        token.RevokedAt = revokedAt;

        return dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Loads and mutates rather than issuing a set-based ExecuteUpdate, and saves rather than
    /// staging. ExecuteUpdate bypasses the change tracker, so rows already loaded in this context
    /// would keep their stale, un-revoked state and a token revoked a moment ago would still be
    /// accepted by the very next lookup. Revocation is a security action; it does not get to depend
    /// on the caller remembering to save.
    /// </summary>
    public async Task RevokeAllForUserAsync(
        int userId,
        DateTime revokedAt,
        CancellationToken cancellationToken = default)
    {
        var live = await dbContext.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in live)
            token.RevokedAt = revokedAt;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
