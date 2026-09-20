using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Repositories.RefreshTokens;
using Webly.Data.Repositories.SecurityTokens;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;
using Webly.Services.Services.Email;
using Webly.Services.Services.Email.Templates;

namespace Webly.Services.UseCases.Authentication;

public class ResetPassword(
    ISecurityTokenService securityTokenService,
    ISecurityTokenRepository securityTokenRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher passwordHasher,
    IAuthSessionService authSessionService,
    IEmailSender emailSender,
    WeblyDbContext dbContext)
{
    public async Task<AuthResult> Execute(
        string rawToken,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var hash = securityTokenService.Hash(rawToken, SecurityTokenPurpose.PasswordReset);
        var token = await securityTokenRepository.FindByHashAsync(hash, cancellationToken);

        var now = DateTime.UtcNow;

        if (token is null || token.ConsumedAt is not null || token.ExpiresAt <= now)
            return AuthResult.Fail(AuthError.InvalidToken);

        token.ConsumedAt = now;
        token.User.PasswordHash = passwordHasher.Hash(newPassword);

        // Reading the mail proves control of the address, which is the same thing verification asks
        // for. Demanding it again afterwards would be asking for the identical proof twice.
        token.User.EmailVerifiedAt ??= now;

        await dbContext.SaveChangesAsync(cancellationToken);

        // A reset is what you do when you suspect someone else is in your account, so every existing
        // session goes — including, deliberately, the one doing the reset.
        await refreshTokenRepository.RevokeAllForUserAsync(token.UserId, now, cancellationToken);

        await emailSender.SendAsync(
            WeblyEmails.PasswordChanged(token.User.Email, token.User.DisplayName),
            cancellationToken);

        return await authSessionService.IssueAsync(token.User, cancellationToken);
    }
}
