namespace Webly.Data.Models.Authentication;

/// <summary>
/// The platform-wide roles that may appear in <see cref="User.Roles"/>, spelled once so a typo
/// cannot quietly grant nothing.
///
/// Nothing to do with site ownership, which carries no roles at all — a site has one owner, who
/// may do everything to it.
/// </summary>
public static class UserRoles
{
    /// <summary>
    /// Administers the platform: curates the site templates and the standing agent instructions they
    /// carry, and may read a failed deployment's provider-side logs. Granted by configuration
    /// (<c>Administrators:Emails</c>), never written by the app — see <c>IAdminPolicy</c>.
    /// </summary>
    public const string Admin = "admin";
}
