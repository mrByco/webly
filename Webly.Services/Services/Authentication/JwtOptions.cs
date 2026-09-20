using System.ComponentModel.DataAnnotations;

namespace Webly.Services.Services.Authentication;

/// <summary>
/// Everything token minting needs, in one place. Bound from the <c>Jwt</c> configuration section
/// and validated at startup, so a deployment missing a key fails loudly at boot instead of quietly
/// at the first login attempt.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Signs the access token. HMAC-SHA512, so anything shorter than 64 bytes weakens it.</summary>
    [Required, MinLength(64)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Keys the HMAC that hashes refresh tokens. Deliberately separate from <see cref="Key"/>:
    /// the two protect different things, and rotating one should not invalidate the other.
    /// </summary>
    [Required, MinLength(64)]
    public string RefreshKey { get; set; } = string.Empty;

    /// <summary>
    /// Short by design. The refresh cookie is what keeps a session alive; a stolen access token
    /// stops working within this window.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a session survives without the user signing in again.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(60);
}
