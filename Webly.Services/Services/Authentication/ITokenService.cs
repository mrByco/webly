using Webly.Data.Models.Authentication;

namespace Webly.Services.Services.Authentication;

public interface ITokenService
{
    /// <summary>
    /// Mints a short-lived signed access token for the user, stamped with the session it belongs to.
    ///
    /// The session id is required rather than optional on purpose: every token minted without one would be a
    /// token nothing can revoke before it expires, and the compiler is a better place to be reminded of that
    /// than a code review. See <see cref="RefreshToken.SessionId"/>.
    /// </summary>
    string CreateAccessToken(User user, string sessionId);

    /// <summary>
    /// Creates a refresh token. Returns the raw value (which goes to the browser and is never
    /// persisted) alongside the row to store (which holds only its hash).
    /// </summary>
    /// <param name="sessionId">
    /// The session this token continues, or null to begin one. A rotation continues; a sign-in begins.
    /// </param>
    (string RawToken, RefreshToken Row) CreateRefreshToken(User user, string? sessionId = null);

    /// <summary>Hashes a raw refresh token the same way <see cref="CreateRefreshToken"/> did, for lookup.</summary>
    string HashRefreshToken(string rawToken);
}
