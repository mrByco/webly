using Webly.Data.Models.Authentication;

namespace Webly.Data.Repositories.Users;

public static class UserRepositoryExtensions
{
    /// <summary>
    /// The user behind an id that authentication has already vouched for. Missing is not an expected
    /// outcome here, so it throws rather than handing every caller a null to re-explain.
    /// </summary>
    public static async Task<User> GetRequiredAsync(
        this IUserRepository repository,
        int id,
        CancellationToken cancellationToken = default) =>
        await repository.FindByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Authenticated user {id} has no row.");
}
