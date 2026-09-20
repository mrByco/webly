using Webly.Data.Models.Authentication;

namespace Webly.Data.Repositories.Users;

public interface IUserRepository
{
    /// <summary>Looks up by email, normalizing the argument first. Includes external logins.</summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> FindByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Looks up the account a provider identity is linked to, or null if it is unknown.</summary>
    Task<User?> FindByExternalLoginAsync(
        ExternalLoginProvider provider,
        string providerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Just the pointer to the site the editor is open on. Its own query because the callers that
    /// want only this would otherwise drag the site row and the external logins along for one
    /// nullable int.
    /// </summary>
    Task<int?> FindCurrentSiteIdAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Just the login address. Same reasoning as <see cref="FindCurrentSiteIdAsync"/>: the admin
    /// check wants one string, not the user's site and external logins with it.
    /// </summary>
    Task<string?> FindEmailAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Stages the user for insert. The caller decides when to save.</summary>
    void Add(User user);
}
