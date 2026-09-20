namespace Webly.Services.Services.Authentication;

/// <summary>
/// Access tokens that must be refused before they expire on their own.
///
/// A signed JWT is normally valid until its <c>exp</c> no matter what the server thinks, which
/// would let a logged-out session keep working for the rest of the access-token lifetime. This is
/// the small amount of state that buys back the ability to end a session immediately.
/// </summary>
public interface IAccessTokenBlacklist
{
    /// <summary>
    /// Refuses the token with this <c>jti</c> from now until <paramref name="expiresAt"/>, after
    /// which the entry is pointless — the token is expired anyway — and drops itself.
    /// </summary>
    void Revoke(string tokenId, DateTimeOffset expiresAt);

    /// <summary>
    /// Refuses every access token of a user that still claims their email is unverified.
    ///
    /// Called when they verify. We cannot enumerate the tokens their other devices hold, and we do
    /// not want to sign those out — we want their next request to fetch a token that tells the
    /// truth, which is what the cookie middleware does with a refusal.
    ///
    /// Expressed as "tokens making an outdated claim" rather than "tokens issued before time T" on
    /// purpose: a JWT's <c>iat</c> is only accurate to the second, so a timestamp comparison also
    /// condemns the replacement token minted in that same second, and the session can never recover.
    /// </summary>
    void RevokeUnverifiedTokens(int userId, DateTimeOffset expiresAt);

    /// <param name="tokenSaysEmailVerified">The token's own <c>email_verified</c> claim.</param>
    bool IsRevoked(string tokenId, int userId, bool tokenSaysEmailVerified);
}
