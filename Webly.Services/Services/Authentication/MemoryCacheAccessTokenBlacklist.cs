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

    public bool IsRevoked(string tokenId, int userId, bool tokenSaysEmailVerified)
    {
        if (cache.TryGetValue(TokenKeyPrefix + tokenId, out _))
            return true;

        // Only tokens carrying the outdated claim are caught, so the replacement issued moments
        // later — which does say verified — passes straight through.
        return !tokenSaysEmailVerified && cache.TryGetValue(UnverifiedKeyPrefix + userId, out _);
    }
}
