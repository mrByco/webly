using Webly.Data.Models.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Authentication;

/// <summary>
/// A single-use credential mailed to a user to prove they control their address — either confirming
/// it or resetting the password on it.
///
/// One table for both purposes: identical lifecycle (issue, mail, consume once, expire), so two
/// tables would be the same code twice. Only the hash is stored, exactly as for
/// <see cref="RefreshToken"/> — these arrive in a URL, and a leaked database should not hand anyone
/// a working password-reset link.
/// </summary>
public class UserSecurityToken : IHasTimestamps
{
    [Key]
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public SecurityTokenPurpose Purpose { get; set; }

    /// <summary>HMAC-SHA256 of the raw token, with <see cref="Purpose"/> mixed into the input.</summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// Hash of the short code that goes in the same email, for people who would rather type six
    /// digits than move a link between devices. Null for purposes that have no code.
    ///
    /// The link and the code are two ways to perform one act, so they share a row: whichever is used
    /// consumes both, and a resend retires both together.
    /// </summary>
    public string? CodeHash { get; set; }

    /// <summary>
    /// Codes entered against this one so far, the right one included. Six digits is a space a machine can walk
    /// in a second, so the code dies after a handful of tries and the user has to ask for a new one — the
    /// counter, not the length, is what makes a short code safe.
    ///
    /// <b>Each try is counted before it is compared</b>, in one conditional update, which is why a correct code
    /// counts too. It used to be read, compared and written back, and that order is not a limit: every request
    /// in a burst read the same count, so sixty wrong codes sent at once against the running app left it at four
    /// and the right code was accepted after them. It was called <c>FailedAttempts</c> then.
    /// </summary>
    public int CodeAttempts { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>When it was used, or null while it is still good for one use.</summary>
    public DateTime? ConsumedAt { get; set; }
}
