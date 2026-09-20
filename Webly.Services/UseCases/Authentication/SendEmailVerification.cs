using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Repositories.SecurityTokens;
using Webly.Services.Services.Authentication;
using Webly.Services.Services.Email;
using Webly.Services.Services.Email.Templates;
using Microsoft.Extensions.Options;

namespace Webly.Services.UseCases.Authentication;

/// <summary>
/// Issues a verification link and mails it. Called at registration and from the resend endpoint.
/// </summary>
public class SendEmailVerification(
    ISecurityTokenService securityTokenService,
    ISecurityTokenRepository securityTokenRepository,
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    IOptions<EmailTokenOptions> tokenOptions,
    WeblyDbContext dbContext)
{
    /// <returns>False when the cooldown suppressed the send; the caller still reports success.</returns>
    public async Task<bool> Execute(User user, CancellationToken cancellationToken = default)
    {
        if (user.EmailVerifiedAt is not null)
            return false;

        var latest = await securityTokenRepository.FindLatestAsync(
            user.Id, SecurityTokenPurpose.EmailVerification, cancellationToken);

        // Anyone can trigger this by typing an address, so without a cooldown the endpoint is a
        // free way to flood someone's inbox using our sending reputation.
        if (latest is not null && latest.CreatedAt.Add(tokenOptions.Value.ResendCooldown) > DateTime.UtcNow)
            return false;

        // A new link retires the old one, so a forwarded earlier email stops working.
        await securityTokenRepository.InvalidateOutstandingAsync(
            user.Id, SecurityTokenPurpose.EmailVerification, DateTime.UtcNow, cancellationToken);

        var (rawToken, code, row) = securityTokenService.Create(user, SecurityTokenPurpose.EmailVerification);
        securityTokenRepository.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);

        var link = $"{emailOptions.Value.BaseUrl.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(rawToken)}";

        await emailSender.SendAsync(
            WeblyEmails.VerifyEmail(user.Email, user.DisplayName, link, code!),
            cancellationToken);

        return true;
    }
}
