namespace Webly.Services.Services.Authentication;

/// <summary>
/// Answers whether a caller maintains the global catalog. See <see cref="AdministratorOptions"/>
/// for why this is configuration rather than a stored column.
///
/// Deliberately not a token claim. A claim goes stale exactly the way <c>email_verified</c> did,
/// and recovering from that needed the whole blacklist-invalidation mechanism; admin-ness is
/// checked on a handful of write endpoints, where one projected query is far cheaper than that.
/// </summary>
public interface IAdminPolicy
{
    /// <summary>For a caller whose user is already loaded.</summary>
    bool IsAdmin(string email);

    /// <summary>For a use case holding only an id. One projected query, no entity graph.</summary>
    Task<bool> IsAdminAsync(int userId, CancellationToken cancellationToken = default);
}
