using Webly.Data.Models.Authentication;
using Webly.Services.DTO.Authentication;

namespace Webly.Services.Services.Authentication;

public interface IAuthSessionService
{
    /// <summary>
    /// Starts a session for a user: mints an access token, stores a fresh refresh token, and
    /// returns both alongside the profile. Every sign-in path ends here, so a session created by
    /// registration, by password, by Google or by rotation is the same session in every respect.
    /// </summary>
    Task<AuthResult> IssueAsync(User user, CancellationToken cancellationToken = default);

    MeResponse Describe(User user);
}
