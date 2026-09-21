using Microsoft.Extensions.Caching.Memory;

namespace Webly.Services.Services.Authentication;

/// <summary>
/// In-memory, and deliberately so.
///
/// Entries live at most one access-token lifetime (15 minutes), so the cache stays small and needs
/// no cleanup — each entry expires exactly when the token it blocks would have. The cost is that
/// the list is per-process: a restart forgets it, and a second instance never knew it. Both are
/// bounded by that same 15 minutes, and both are acceptable while Webly runs as one instance. It is
/// the first piece that has to move to shared storage when it does not — see CLAUDE.md.
/// </summary>
public class MemoryCacheAccessTokenBlacklist(IMemoryCache cache) : IAccessTokenBlacklist
{
    private const string TokenKeyPrefix = "revoked-access-token:";
    private const string UnverifiedKeyPrefix = "revoked-unverified-tokens:";
    private const string SessionKeyPrefix = "revoked-session:";

    public void Revoke(string tokenId, DateTimeOffset expiresAt)
    {
        if (expiresAt <= DateTimeOffset.UtcNow)
            return;

        cache.Set(TokenKeyPrefix + tokenId, true, expiresAt);
    }

    public void RevokeUnverifiedTokens(int userId, DateTimeOffset expiresAt)
    {
        if (expiresAt <= DateTimeOffset.UtcNow)
            return;

        cache.Set(UnverifiedKeyPrefix + userId, true, expiresAt);
    }

    public void RevokeSession(string sessionId, DateTimeOffset until)
    {
        if (until <= DateTimeOffset.UtcNow)
            return;

        // Kept until the session's own refresh token would have expired, which is the upper bound on anything
        // that session could have minted. That is up to sixty days per sign-out, in memory, and is the sharpest
        // version of what this whole class already admits: it is per process, so a restart forgets it and a
        // second instance never knew. Moving it to shared storage is the same piece of work for all three
        // entries here.
        cache.Set(SessionKeyPrefix + sessionId, true, until);
    }

    public bool IsSessionRevoked(string sessionId) => cache.TryGetValue(SessionKeyPrefix + sessionId, out _);

    public bool IsRevoked(string tokenId, int userId, bool tokenSaysEmailVerified)
    {
        if (cache.TryGetValue(TokenKeyPrefix + tokenId, out _))
            return true;

        // Only tokens carrying the outdated claim are caught, so the replacement issued moments
        // later — which does say verified — passes straight through.
        return !tokenSaysEmailVerified && cache.TryGetValue(UnverifiedKeyPrefix + userId, out _);
    }
}
