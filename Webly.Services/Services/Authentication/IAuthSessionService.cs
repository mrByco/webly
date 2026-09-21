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
    /// <param name="continuingSessionId">
    /// The session these new tokens continue, or null to begin one. A rotation continues — the tokens change and
    /// the session does not, which is the whole point of <see cref="RefreshToken.SessionId"/> — and everything
    /// else here is somebody signing in.
    /// </param>
    Task<AuthResult> IssueAsync(
        User user,
        string? continuingSessionId = null,
        CancellationToken cancellationToken = default);

    MeResponse Describe(User user);
}
