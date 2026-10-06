using Webly.Data;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Authentication;

namespace Webly.Services.UseCases.Authentication;

/// <summary>
/// Changes the name somebody is called by.
///
/// There was no way to, and the name is not decoration: every email opens with it, the sidebar shows it, and it is
/// the author of every version the person's messages produce. A typo made in the first minute of signing up was in
/// all of those for good. Versions already committed keep the name they were made under — a commit is history —
/// and everything from now on uses the new one.
///
/// Unverified callers may use it, as they may change their password: the verification email itself greets them
/// by this name.
/// </summary>
public class ChangeName(
    IUserRepository userRepository,
    IAuthSessionService authSessionService,
    WeblyDbContext dbContext)
{
    /// <returns>The caller's profile with the new name, or null when the name is only whitespace.</returns>
    public async Task<MeResponse?> Execute(int userId, string displayName, CancellationToken cancellationToken = default)
    {
        var name = displayName.Trim();

        if (name.Length == 0) return null;

        var user = await userRepository.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} disappeared while changing their name.");

        user.DisplayName = name;
        await dbContext.SaveChangesAsync(cancellationToken);

        return authSessionService.Describe(user);
    }
}
