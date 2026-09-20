using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;

namespace Webly.Services.UseCases.Authentication;

public class SignInWithPassword(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IAuthSessionService authSessionService)
{
    public async Task<AuthResult> Execute(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.FindByEmailAsync(request.Email, cancellationToken);

        // An unknown email, a wrong password and a Google-only account (no password set) all give
        // the same answer. Distinguishing them would turn this endpoint into a way to discover who
        // has an account here.
        if (user?.PasswordHash is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
            return AuthResult.Fail(AuthError.InvalidCredentials);

        return await authSessionService.IssueAsync(user, cancellationToken);
    }
}
