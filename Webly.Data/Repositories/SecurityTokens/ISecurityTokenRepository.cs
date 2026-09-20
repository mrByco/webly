using Webly.Data.Models.Authentication;

namespace Webly.Data.Repositories.SecurityTokens;

public interface ISecurityTokenRepository
{
    /// <summary>
    /// Finds a token by hash whether or not it is live. The caller decides what an expired or
    /// already-consumed one means, so this must not filter them away.
    /// </summary>
    Task<UserSecurityToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    void Add(UserSecurityToken token);

    /// <summary>
    /// The live token of a purpose for one user, or null. Codes are looked up by owner rather than
    /// by hash: six digits guessed against every account at once would be a far easier attack than
    /// six digits guessed against one, and only this shape rules it out.
    /// </summary>
    Task<UserSecurityToken?> FindOutstandingAsync(
        int userId,
        SecurityTokenPurpose purpose,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes every outstanding token of one purpose for a user. Issuing a new link should retire
    /// the previous one, so a forwarded old email cannot still be used.
    /// </summary>
    Task InvalidateOutstandingAsync(
        int userId,
        SecurityTokenPurpose purpose,
        DateTime consumedAt,
        CancellationToken cancellationToken = default);

    /// <summary>The most recently issued token of a purpose, used to throttle re-sends.</summary>
    Task<UserSecurityToken?> FindLatestAsync(
        int userId,
        SecurityTokenPurpose purpose,
        CancellationToken cancellationToken = default);
}
