using Webly.Data.Models.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Webly.Services.Services.Authentication;

public class SecurityTokenService(IOptions<EmailTokenOptions> options) : ISecurityTokenService
{
    private readonly EmailTokenOptions _options = options.Value;

    public IssuedSecurityToken Create(User user, SecurityTokenPurpose purpose)
    {
        var rawToken = CreateRawToken();

        // Verification is the one flow where the mail may be read on a different device from the one
        // waiting — a phone holding the inbox, a laptop holding the session. A code crosses that gap
        // by hand. A reset gets no code: it hands over an account, so it stays link-only.
        var code = purpose is SecurityTokenPurpose.EmailVerification ? CreateCode() : null;

        var row = new UserSecurityToken
        {
            User = user,
            Purpose = purpose,
            TokenHash = Hash(rawToken, purpose),
            CodeHash = code is null ? null : HashCode(code, user.Id, purpose),
            ExpiresAt = DateTime.UtcNow.Add(LifetimeOf(purpose))
        };

        return new IssuedSecurityToken(rawToken, code, row);
    }

    public IssuedRawToken IssueRaw(SecurityTokenPurpose purpose)
    {
        var rawToken = CreateRawToken();

        return new IssuedRawToken(
            rawToken,
            Hash(rawToken, purpose),
            DateTime.UtcNow.Add(LifetimeOf(purpose)));
    }

    /// <summary>
    /// URL-safe: this value's only job is to survive being pasted into a link and back out of a mail
    /// client that may have wrapped the line, or being copied by hand out of a chat message.
    /// </summary>
    private static string CreateRawToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Six digits, uniformly drawn — <see cref="RandomNumberGenerator.GetInt32(int, int)"/> rather
    /// than a modulo of random bytes, which would quietly favour the low end of the range.
    /// </summary>
    private static string CreateCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>
    /// A keyed HMAC, like refresh tokens: lookup is by hash, which rules out a per-row salt, and the
    /// key is what stops someone holding a copy of the table from producing a matching value.
    ///
    /// The purpose is mixed into the hashed input, so the same raw string hashes differently for a
    /// verification than for a reset. A verification token can therefore never be presented as a
    /// password reset even if it leaks — the two namespaces simply do not meet.
    /// </summary>
    public string Hash(string rawToken, SecurityTokenPurpose purpose) =>
        Compute($"{purpose}:{rawToken}");

    public string HashCode(string code, int userId, SecurityTokenPurpose purpose) =>
        Compute($"{purpose}:code:{userId}:{code}");

    private string Compute(string input)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.Key));

        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(input)));
    }

    private TimeSpan LifetimeOf(SecurityTokenPurpose purpose) => purpose switch
    {
        SecurityTokenPurpose.EmailVerification => _options.VerificationLifetime,
        SecurityTokenPurpose.PasswordReset => _options.PasswordResetLifetime,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose))
    };

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
