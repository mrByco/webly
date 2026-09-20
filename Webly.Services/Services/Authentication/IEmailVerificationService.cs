using Webly.Data.Models.Authentication;
using Webly.Services.DTO.Authentication;

namespace Webly.Services.Services.Authentication;

public interface IEmailVerificationService
{
    /// <summary>
    /// The half of verifying that is the same whichever credential was presented: consume it, stamp
    /// the address as proven, make the user's other devices notice, and hand back a fresh session.
    ///
    /// Shared rather than written twice, because a link and a typed code are two doors into one act
    /// and the two must not be able to drift into doing subtly different things.
    /// </summary>
    Task<AuthResult> CompleteAsync(UserSecurityToken token, CancellationToken cancellationToken = default);
}
