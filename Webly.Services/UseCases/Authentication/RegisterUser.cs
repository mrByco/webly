using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;

namespace Webly.Services.UseCases.Authentication;

/// <summary>
/// Creates a local account and signs it straight in.
///
/// The new user gets no site: <c>CurrentSiteId</c> stays null until they create one, and
/// <see cref="MeResponse.HasSite"/> is how the client knows to offer that instead of the editor.
/// </summary>
public class RegisterUser(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IAuthSessionService authSessionService,
    SendEmailVerification sendEmailVerification,
    WeblyDbContext dbContext)
{
    public async Task<AuthResult> Execute(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = NormalizedEmail.From(request.Email);

        if (await userRepository.FindByEmailAsync(email, cancellationToken) is not null)
            return AuthResult.Fail(AuthError.EmailAlreadyRegistered);

        var user = new User
        {
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password)
        };

        userRepository.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        await sendEmailVerification.Execute(user, cancellationToken);

        // Signed in immediately: verification gates reaching the app, not getting through the
        // door. Making people wait for an email before they can see anything is how sign-ups are lost.
        return await authSessionService.IssueAsync(user, cancellationToken);
    }
}
