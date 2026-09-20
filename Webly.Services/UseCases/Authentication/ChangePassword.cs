using Webly.Data;
using Webly.Data.Repositories.RefreshTokens;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;
using Webly.Services.Services.Email;
using Webly.Services.Services.Email.Templates;

namespace Webly.Services.UseCases.Authentication;

/// <summary>
/// Changes the password of a signed-in user, and doubles as the way a Google-created account sets
/// its first one.
/// </summary>
public class ChangePassword(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher passwordHasher,
    IAuthSessionService authSessionService,
    IEmailSender emailSender,
    WeblyDbContext dbContext)
{
    public async Task<AuthResult> Execute(
        int userId,
        string? currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);

        if (user is null)
            return AuthResult.Fail(AuthError.InvalidCredentials);

        if (user.PasswordHash is null)
        {
            // Nothing to prove knowledge of — this account was created through Google. The verified
            // address is what makes that safe: it means the person holding this session already
            // demonstrated they own the mailbox, so letting them add a password grants nothing they
            // could not get through a password reset anyway.
            if (user.EmailVerifiedAt is null)
                return AuthResult.Fail(AuthError.EmailNotVerified);
        }
        else if (currentPassword is null || !passwordHasher.Verify(currentPassword, user.PasswordHash))
        {
            return AuthResult.Fail(AuthError.InvalidCredentials);
        }

        user.PasswordHash = passwordHasher.Hash(newPassword);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Every session drops, then the caller gets a fresh one below — so a stolen session
        // elsewhere dies, and the person who made the change stays signed in.
        await refreshTokenRepository.RevokeAllForUserAsync(userId, DateTime.UtcNow, cancellationToken);

        await emailSender.SendAsync(
            WeblyEmails.PasswordChanged(user.Email, user.DisplayName),
            cancellationToken);

        return await authSessionService.IssueAsync(user, cancellationToken);
    }
}
