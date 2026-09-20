using Webly.Data.Models.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Authentication;

/// <summary>
/// A link between a Webly account and an identity at an external provider.
///
/// One user may have several (a password plus Google, or two providers), which is why this is its
/// own table rather than a pair of columns on <see cref="User"/>. Sign-in matches on
/// <see cref="ProviderKey"/> — the provider's stable subject id — not on email, so a user who
/// changes their Google address keeps the same Webly account.
/// </summary>
public class ExternalLogin : IHasTimestamps
{
    [Key]
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public ExternalLoginProvider Provider { get; set; }

    /// <summary>
    /// The provider's immutable identifier for this person (Google's `sub`). Never their email:
    /// emails get reassigned, subjects do not.
    /// </summary>
    public required string ProviderKey { get; set; }
}
