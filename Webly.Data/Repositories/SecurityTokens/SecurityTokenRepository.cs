using Webly.Data.Models.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Webly.Data.Repositories.SecurityTokens;

public class SecurityTokenRepository(WeblyDbContext dbContext) : ISecurityTokenRepository
{
    public Task<UserSecurityToken?> FindByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        dbContext.UserSecurityTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

    public void Add(UserSecurityToken token) => dbContext.UserSecurityTokens.Add(token);

    public Task<UserSecurityToken?> FindOutstandingAsync(
        int userId,
        SecurityTokenPurpose purpose,
        DateTime now,
        CancellationToken cancellationToken = default) =>
        dbContext.UserSecurityTokens
            .Include(x => x.User)
            .Where(x => x.UserId == userId
                && x.Purpose == purpose
                && x.ConsumedAt == null
                && x.ExpiresAt > now)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Loads and mutates rather than issuing a set-based update, for the same reason
    /// <see cref="RefreshTokens.RefreshTokenRepository.RevokeAllForUserAsync"/> does: ExecuteUpdate
    /// bypasses the change tracker, and a token invalidated a moment ago would still look live to
    /// anything already holding it.
    /// </summary>
    public async Task InvalidateOutstandingAsync(
        int userId,
        SecurityTokenPurpose purpose,
        DateTime consumedAt,
        CancellationToken cancellationToken = default)
    {
        var outstanding = await dbContext.UserSecurityTokens
            .Where(x => x.UserId == userId && x.Purpose == purpose && x.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in outstanding)
            token.ConsumedAt = consumedAt;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<UserSecurityToken?> FindLatestAsync(
        int userId,
        SecurityTokenPurpose purpose,
        CancellationToken cancellationToken = default) =>
        dbContext.UserSecurityTokens
            .Where(x => x.UserId == userId && x.Purpose == purpose)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
