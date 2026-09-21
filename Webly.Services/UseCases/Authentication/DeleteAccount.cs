using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Webly.Data;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Authentication;
using Webly.Services.DTO.Common;
using Webly.Services.Services.Authentication;
using Webly.Services.UseCases.Sites;

namespace Webly.Services.UseCases.Authentication;

/// <summary>
/// Closes an account and everything under it.
///
/// <b>The sites go first, one at a time, through <see cref="DeleteSite"/>.</b> Not because the database needs it
/// to — the cascade would take the rows — but because a site is more than rows: a bare git repository on disk, a
/// warm sandbox, a project at the hosting provider. Only that use case knows about all three, and the alternative
/// is a second place that has to remember them, which is how the orphans start.
///
/// <b>Then the user row, with <c>ExecuteDelete</c>.</b> EF cannot do this through the change tracker at all: a
/// conversation message points at the version it produced and that version points back at the message, so the
/// client-side cascade reports a circular dependency and sends nothing. Postgres does it in one statement with the
/// deferred constraints the migration writes by hand — which is what
/// <c>WeblyDbContextTests.Deleting_a_user_removes_their_sites_and_everything_under_them</c> exists to keep true.
///
/// <b>The password is proved again.</b> A signed-in session is enough for everything else in the product because
/// everything else is undoable; this is not. An account created through Google has no password to prove, and the
/// verified address is what makes that safe — the same reasoning as <see cref="ChangePassword"/>.
/// </summary>
public class DeleteAccount(
    IUserRepository userRepository,
    ISiteRepository siteRepository,
    IPasswordHasher passwordHasher,
    DeleteSite deleteSite,
    WeblyDbContext dbContext,
    ILogger<DeleteAccount> logger)
{
    public async Task<Result<AuthError>> ExecuteAsync(
        int userId,
        string? currentPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);

        if (user is null) return Result<AuthError>.Fail(AuthError.InvalidCredentials);

        if (user.PasswordHash is not null
            && (currentPassword is null || !passwordHasher.Verify(currentPassword, user.PasswordHash)))
            return Result<AuthError>.Fail(AuthError.InvalidCredentials);

        foreach (var site in await siteRepository.ListForOwnerAsync(userId, cancellationToken))
        {
            var deleted = await deleteSite.ExecuteAsync(userId, site.Nanoid, cancellationToken);

            // Logged and carried on with. A provider that will not answer must not be able to keep somebody's
            // account open — the row is going either way, and an orphaned project costs money and is visible in a
            // dashboard, while an account that cannot be closed costs trust.
            if (!deleted.Succeeded)
                logger.LogWarning(
                    "Deleting site {Site} while closing account {User} failed: {Error}.",
                    site.Nanoid, userId, deleted.Error);
        }

        await dbContext.Users.Where(x => x.Id == userId).ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Account {User} was closed.", userId);

        return Result<AuthError>.Ok();
    }
}
