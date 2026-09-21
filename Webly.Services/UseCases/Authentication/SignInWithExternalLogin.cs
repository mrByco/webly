using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Repositories.RefreshTokens;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;
using Webly.Services.Services.Email;
using Webly.Services.Services.Email.Templates;

namespace Webly.Services.UseCases.Authentication;

/// <summary>
/// Signs in through an external provider, resolving which account that identity belongs to.
///
/// Three cases, in order — the order is the design:
/// 1. The provider identity is already linked → sign that user in. Matching on the provider's
///    subject rather than the email is what lets someone change their Google address and keep
///    their Webly account.
/// 2. No link, but the email is a known account → attach the link to it. This is what makes
///    "one account, two doors" true, and it is the behaviour the product asked for.
/// 3. Neither → create an account with no password and no site.
/// </summary>
public class SignInWithExternalLogin(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IAuthSessionService authSessionService,
    IEmailSender emailSender,
    WeblyDbContext dbContext)
{
    public async Task<AuthResult> Execute(ExternalLoginInfo info, CancellationToken cancellationToken = default)
    {
        var linked = await userRepository.FindByExternalLoginAsync(info.Provider, info.ProviderKey, cancellationToken);

        if (linked is not null)
            return await authSessionService.IssueAsync(linked, cancellationToken: cancellationToken);

        // Past this point the email is what decides which account this identity joins or becomes,
        // so an address the provider has not verified is worthless: anyone can put someone else's
        // address on an account they control and walk into it.
        if (!info.EmailVerified)
            return AuthResult.Fail(AuthError.ExternalEmailNotVerified);

        var email = NormalizedEmail.From(info.Email);
        var user = await userRepository.FindByEmailAsync(email, cancellationToken);
        var now = DateTime.UtcNow;

        var takingOverUnverifiedAccount = user is not null
            && user.EmailVerifiedAt is null
            && user.PasswordHash is not null;

        if (user is null)
        {
            user = new User
            {
                Email = email,
                DisplayName = string.IsNullOrWhiteSpace(info.DisplayName) ? email : info.DisplayName.Trim(),
                ProfilePictureUrl = info.ProfilePictureUrl,

                // The provider already confirmed this address; asking the user to confirm it again
                // would be asking for proof we have just been given.
                EmailVerifiedAt = now
            };

            userRepository.Add(user);
        }

        if (takingOverUnverifiedAccount)
        {
            // The account existed with a password, but nobody ever proved they owned this address —
            // anyone can type someone else's email into a registration form. The person signing in
            // now *has* proved it, via the provider. So the address's real owner takes the account
            // and whoever set that password loses their way in.
            //
            // Without this, registering with a stranger's address and waiting would be a way to
            // inherit their account the moment they signed in with Google.
            user!.PasswordHash = null;
            await refreshTokenRepository.RevokeAllForUserAsync(user.Id, now, cancellationToken);
        }

        user!.EmailVerifiedAt ??= now;

        var login = new ExternalLogin
        {
            User = user,
            Provider = info.Provider,
            ProviderKey = info.ProviderKey
        };

        user.ExternalLogins.Add(login);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (takingOverUnverifiedAccount)
        {
            await emailSender.SendAsync(
                WeblyEmails.PasswordRemoved(user.Email, user.DisplayName),
                cancellationToken);
        }

        return await authSessionService.IssueAsync(user, cancellationToken: cancellationToken);
    }
}
