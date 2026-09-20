using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Repositories.SecurityTokens;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Webly.Services.UseCases.Authentication;

/// <summary>
/// Verifies an address from the six digits in the email, for the common case where the mail is read
/// on a phone while the session waits on a laptop.
///
/// Unlike the link, this needs the caller to be signed in. The code is found by owner, never by
/// value: a six-digit space searched against every account at once is a far cheaper attack than the
/// same space searched against one, and requiring the session is what removes that option entirely.
/// </summary>
public class VerifyEmailWithCode(
    ISecurityTokenService securityTokenService,
    ISecurityTokenRepository securityTokenRepository,
    IEmailVerificationService emailVerificationService,
    IOptions<EmailTokenOptions> tokenOptions,
    WeblyDbContext dbContext)
{
    public async Task<AuthResult> Execute(
        int userId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var token = await securityTokenRepository.FindOutstandingAsync(
            userId, SecurityTokenPurpose.EmailVerification, DateTime.UtcNow, cancellationToken);

        if (token?.CodeHash is null || token.FailedAttempts >= tokenOptions.Value.MaxCodeAttempts)
            return AuthResult.Fail(AuthError.InvalidToken);

        var presented = securityTokenService.HashCode(
            code.Trim(), userId, SecurityTokenPurpose.EmailVerification);

        // Fixed-time comparison. The window it closes is narrow, but this is a six-digit secret being
        // compared on a public endpoint, and a byte-at-a-time exit would shrink the search to sixty
        // guesses. Cheap enough that not doing it needs a reason.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(token.CodeHash)))
        {
            // Counted, not just refused: the attempt limit is the only thing standing between six
            // digits and a machine, so a wrong guess has to cost something.
            token.FailedAttempts++;
            await dbContext.SaveChangesAsync(cancellationToken);

            return AuthResult.Fail(AuthError.InvalidToken);
        }

        return await emailVerificationService.CompleteAsync(token, cancellationToken);
    }
}
