using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Repositories.SecurityTokens;
using Webly.Data.Repositories.Users;
using Webly.Services.Services.Authentication;
using Webly.Services.Services;
using Webly.Services.Services.Email;
using Webly.Services.Services.Email.Templates;
using Microsoft.Extensions.Options;

namespace Webly.Services.UseCases.Authentication;

/// <summary>
/// Mails a password-reset link, if there is anyone to mail it to.
///
/// This use case returns nothing and never fails. Whether the address is registered is precisely
/// what an attacker wants to learn here, so the endpoint must answer the same way either way — and
/// the easiest way to guarantee that is to give the caller nothing to leak.
/// </summary>
public class RequestPasswordReset(
    IUserRepository userRepository,
    ISecurityTokenService securityTokenService,
    ISecurityTokenRepository securityTokenRepository,
    IEmailSender emailSender,
    IOptions<AppOptions> app,
    IOptions<EmailTokenOptions> tokenOptions,
    WeblyDbContext dbContext)
{
    public async Task Execute(string email, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.FindByEmailAsync(email, cancellationToken);

        if (user is null)
            return;

        var latest = await securityTokenRepository.FindLatestAsync(
            user.Id, SecurityTokenPurpose.PasswordReset, cancellationToken);

        if (latest is not null && latest.CreatedAt.Add(tokenOptions.Value.ResendCooldown) > DateTime.UtcNow)
            return;

        // Only the newest link may work: an inbox with three reset mails in it should not be three
        // live keys to the account.
        await securityTokenRepository.InvalidateOutstandingAsync(
            user.Id, SecurityTokenPurpose.PasswordReset, DateTime.UtcNow, cancellationToken);

        var (rawToken, _, row) = securityTokenService.Create(user, SecurityTokenPurpose.PasswordReset);
        securityTokenRepository.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);

        var link = $"{app.Value.Origin}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        await emailSender.SendAsync(
            WeblyEmails.ResetPassword(user.Email, user.DisplayName, link),
            cancellationToken);
    }
}
