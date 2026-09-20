using Webly.Data.Models.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Webly.Services.Services.Authentication;

public class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    /// <summary>
    /// Claim carrying the internal user id. Named plainly rather than using a schema URI so it
    /// survives round-tripping without the handler's inbound claim mapping rewriting it.
    /// </summary>
    public const string UserIdClaim = "uid";

    /// <summary>Whether the user had proven their email address when this token was minted.</summary>
    public const string EmailVerifiedClaim = "email_verified";

    private readonly JwtOptions _options = options.Value;

    public string CreateAccessToken(User user)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(UserIdClaim, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Sub, user.Nanoid),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),

                // Carried in the token so the verified-only accessors cost nothing per request. It
                // goes stale when someone verifies elsewhere, which is what the blacklist's
                // per-user cutoff exists to fix: their next request refreshes into a truthful one.
                new Claim(EmailVerifiedClaim, (user.EmailVerifiedAt is not null).ToString().ToLowerInvariant())
            ]),
            Expires = DateTime.UtcNow.Add(_options.AccessTokenLifetime),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha512)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public (string RawToken, RefreshToken Row) CreateRefreshToken(User user)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        var row = new RefreshToken
        {
            User = user,
            TokenHash = HashRefreshToken(rawToken),
            ExpiresAt = DateTime.UtcNow.Add(_options.RefreshTokenLifetime)
        };

        return (rawToken, row);
    }

    /// <summary>
    /// A keyed HMAC, not a salted password hash: lookup has to find the row by hash, which rules
    /// out a per-row salt. The key is what stops someone with a copy of the table from producing a
    /// matching hash, and it is separate from the JWT signing key on purpose.
    /// </summary>
    public string HashRefreshToken(string rawToken)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.RefreshKey));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawToken)));
    }

    /// <summary>
    /// UTF8 here and UTF8 in the validation parameters. The reference project encoded this key as
    /// ASCII when minting and UTF8 when validating, which works only until a key contains a
    /// non-ASCII byte and then fails as an unexplained "invalid signature".
    /// </summary>
    private SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(_options.Key));
}
