using Webly.Data.Models.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Webly.Data.Repositories.Users;

public class UserRepository(WeblyDbContext dbContext) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizedEmail.From(email);

        return dbContext.Users
            .Include(x => x.CurrentSite)
            .Include(x => x.ExternalLogins)
            .FirstOrDefaultAsync(x => x.Email == normalized, cancellationToken);
    }

    public Task<User?> FindByIdAsync(int id, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Include(x => x.CurrentSite)
            .Include(x => x.ExternalLogins)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<User?> FindByExternalLoginAsync(
        ExternalLoginProvider provider,
        string providerKey,
        CancellationToken cancellationToken = default) =>
        // Queried from Users rather than projected out of ExternalLogins: Include cannot follow a
        // Select that changes the entity type.
        dbContext.Users
            .Include(x => x.CurrentSite)
            .Include(x => x.ExternalLogins)
            .FirstOrDefaultAsync(
                x => x.ExternalLogins.Any(login => login.Provider == provider && login.ProviderKey == providerKey),
                cancellationToken);

    public Task<int?> FindCurrentSiteIdAsync(int userId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Where(x => x.Id == userId)
            .Select(x => x.CurrentSiteId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<string?> FindEmailAsync(int userId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Where(x => x.Id == userId)
            .Select(x => x.Email)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(User user) => dbContext.Users.Add(user);
}
