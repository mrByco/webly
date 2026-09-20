using Webly.Data.Models.Authentication;

namespace Webly.Services.Services.Authentication;

public interface ITokenService
{
    /// <summary>Mints a short-lived signed access token for the user.</summary>
    string CreateAccessToken(User user);

    /// <summary>
    /// Creates a refresh token. Returns the raw value (which goes to the browser and is never
    /// persisted) alongside the row to store (which holds only its hash).
    /// </summary>
    (string RawToken, RefreshToken Row) CreateRefreshToken(User user);

    /// <summary>Hashes a raw refresh token the same way <see cref="CreateRefreshToken"/> did, for lookup.</summary>
    string HashRefreshToken(string rawToken);
}
