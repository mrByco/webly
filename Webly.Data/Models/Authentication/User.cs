using Webly.Data.Models.Interfaces;
using Webly.Data.Models.Sites;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Authentication;

/// <summary>
/// A person with an account. Everything a person builds hangs off <see cref="Sites"/>, and a site
/// has exactly one owner — there are no memberships and no roles inside a site. See CLAUDE.md
/// "Sites and ownership" for why, and for the shape that makes collaboration cheap to add later.
///
/// Webly owns its identities rather than delegating them to a provider: <see cref="Email"/> is the
/// login, <see cref="PasswordHash"/> the local credential, and an external identity gets a row in
/// <see cref="ExternalLogins"/> rather than a string column here.
/// </summary>
public class User : IHasNanoid, IHasTimestamps
{
    [Key]
    public int Id { get; set; }

    public string Nanoid { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Login identity. Unique, stored lowercase — normalize before writing.</summary>
    public required string Email { get; set; }

    /// <summary>Name the app greets them by, and the name on their sites' author metadata.</summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Hash of the local password (algorithm and parameters encoded in the value itself).
    /// Null for an account that has no local credential — one created by signing in with Google.
    /// </summary>
    public string? PasswordHash { get; set; }

    public string? ProfilePictureUrl { get; set; }

    /// <summary>
    /// When this address was proven to belong to the user — by clicking a mailed link, by resetting
    /// the password through one, or by signing in with a provider that had already verified it.
    /// Null means unproven, and nothing in the app is reachable until it is proven: a site publishes
    /// to the public internet under our infrastructure, so an unowned mailbox never gets that far.
    ///
    /// A timestamp rather than a bool, like <see cref="RefreshToken.RevokedAt"/> — it answers "when".
    /// </summary>
    public DateTime? EmailVerifiedAt { get; set; }

    /// <summary>
    /// Platform-wide roles (e.g. <c>admin</c>, who curates the template gallery and can read a
    /// failing deployment's provider logs). Nothing to do with site ownership, which carries no
    /// roles at all — a site has one owner. Maps to a Postgres <c>text[]</c>.
    /// </summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>
    /// Which site the editor opens on. A person may own several, and the whole editor — the chat,
    /// the version history, the domains — is scoped to one at a time, so the app needs somewhere to
    /// remember which. Cookta's <c>CurrentFamilyId</c>, in the same role.
    ///
    /// Null for a brand-new account until their first site exists, which is what
    /// <c>MeResponse.HasSite</c> reports and what the onboarding screen branches on; set back to null
    /// when that site is deleted. Nothing maintains this pointer on delete except the code that
    /// deletes — see CLAUDE.md.
    /// </summary>
    public int? CurrentSiteId { get; set; }
    public Site? CurrentSite { get; set; }

    /// <summary>Every site this person owns.</summary>
    public ICollection<Site> Sites { get; set; } = [];

    /// <summary>Issued refresh tokens, live and revoked. See <see cref="RefreshToken"/>.</summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    /// <summary>Mailed single-use tokens: email verification and password reset.</summary>
    public ICollection<UserSecurityToken> SecurityTokens { get; set; } = [];

    /// <summary>
    /// External identities that sign in to this account. A user may have both a password and a
    /// Google link — the two are doors into one account, matched on the email address.
    /// </summary>
    public ICollection<ExternalLogin> ExternalLogins { get; set; } = [];
}
