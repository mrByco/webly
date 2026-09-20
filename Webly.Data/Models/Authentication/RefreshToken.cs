using Webly.Data.Models.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Authentication;

/// <summary>
/// One issued refresh token. The raw token is handed to the browser in a cookie and never stored;
/// this row keeps only a keyed hash of it, so a database leak yields nothing a thief can present.
///
/// Tokens rotate on every use: the presented row is revoked and a fresh one inserted. The old rows
/// are kept rather than deleted because they are what makes reuse detection possible — a request
/// carrying an already-revoked token means the cookie leaked, and the user's whole chain is dropped.
/// </summary>
public class RefreshToken : IHasTimestamps
{
    [Key]
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>HMAC-SHA256 of the raw token, keyed with a secret separate from the JWT signing key.</summary>
    public required string TokenHash { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When the token stopped being usable, or null while it is live. A timestamp rather than a
    /// bool: "revoked at 14:02, two minutes after it was issued" is a story, "true" is not.
    /// </summary>
    public DateTime? RevokedAt { get; set; }
}
