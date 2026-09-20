using Webly.Data.Models.Authentication;

namespace Webly.Services.Services.Authentication;

/// <summary>
/// What was just issued: the raw values, which exist only for the length of this call, alongside the
/// row that keeps nothing but their hashes.
/// </summary>
/// <param name="Code">
/// The short typed alternative to the link, or null for a purpose that has none.
/// </param>
public record IssuedSecurityToken(string RawToken, string? Code, UserSecurityToken Row);

/// <summary>
/// A credential for a caller that keeps the hash in a table of its own. Raw value, hash and expiry
/// arrive together so no caller can pair a purpose with the wrong lifetime.
/// </summary>
public record IssuedRawToken(string RawToken, string Hash, DateTime ExpiresAt);

public interface ISecurityTokenService
{
    /// <summary>
    /// Creates a single-use credential for a purpose. Whether it also carries a short code is decided
    /// here, by purpose, rather than by each caller passing a flag — so every verification email has
    /// a code and no reset email ever grows one by accident.
    /// </summary>
    IssuedSecurityToken Create(User user, SecurityTokenPurpose purpose);

    string Hash(string rawToken, SecurityTokenPurpose purpose);

    /// <summary>
    /// Hashes a typed code. The owner is mixed in as well as the purpose, so the same six digits
    /// held by two people are two different values and no comparison can ever cross accounts.
    /// </summary>
    string HashCode(string code, int userId, SecurityTokenPurpose purpose);

    /// <summary>
    /// Issues a credential for a caller that stores the hash in a table of its own rather than in
    /// <see cref="UserSecurityToken"/>. Nothing needs it yet; it is the seam a future credential that
    /// hangs off something other than a user (a share link to a preview, say) would use, so that it
    /// gets the same purpose-mixed hashing rather than inventing its own.
    /// </summary>
    IssuedRawToken IssueRaw(SecurityTokenPurpose purpose);
}
