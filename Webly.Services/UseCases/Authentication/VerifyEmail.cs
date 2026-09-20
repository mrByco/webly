using Webly.Data.Models.Authentication;
using Webly.Data.Repositories.SecurityTokens;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;

namespace Webly.Services.UseCases.Authentication;

/// <summary>
/// Consumes a mailed verification link and marks the address proven. Anonymous: the link may well be
/// opened on a device that has never signed in.
/// </summary>
public class VerifyEmail(
    ISecurityTokenService securityTokenService,
    ISecurityTokenRepository securityTokenRepository,
    IEmailVerificationService emailVerificationService)
{
    /// <returns>
    /// A fresh session on success, so the browser that clicked the link gets cookies whose token
    /// says verified straight away rather than after the next refresh.
    /// </returns>
    public async Task<AuthResult> Execute(string rawToken, CancellationToken cancellationToken = default)
    {
        var hash = securityTokenService.Hash(rawToken, SecurityTokenPurpose.EmailVerification);
        var token = await securityTokenRepository.FindByHashAsync(hash, cancellationToken);

        if (token is null || token.ConsumedAt is not null || token.ExpiresAt <= DateTime.UtcNow)
            return AuthResult.Fail(AuthError.InvalidToken);

        return await emailVerificationService.CompleteAsync(token, cancellationToken);
    }
}
