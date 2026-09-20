using System.ComponentModel.DataAnnotations;

namespace Webly.Services.Services.Authentication;

/// <summary>
/// Keys and lifetimes for the single-use tokens that go out by email. Its own key, separate from the
/// JWT ones, on the same principle: different secrets protect different things, and rotating one
/// should not silently invalidate the others.
/// </summary>
public class EmailTokenOptions
{
    public const string SectionName = "EmailTokens";

    [Required, MinLength(64)]
    public string Key { get; set; } = string.Empty;

    /// <summary>Generous: people read mail hours later, and a resend is a support cost.</summary>
    public TimeSpan VerificationLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Short: a reset link is the single most valuable thing we ever mail, and unlike a verification
    /// link the person is standing at their keyboard waiting for it.
    /// </summary>
    public TimeSpan PasswordResetLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Wrong codes allowed before the credential dies and a new mail is needed. Six digits is a
    /// space a machine walks instantly, so this counter — not the length — is what protects it.
    /// </summary>
    public int MaxCodeAttempts { get; set; } = 5;

    /// <summary>How long before another mail of the same purpose may be issued.</summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);
}
