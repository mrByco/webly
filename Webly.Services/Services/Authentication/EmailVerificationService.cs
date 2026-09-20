using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Services.DTO.Authentication;
using Microsoft.Extensions.Options;

namespace Webly.Services.Services.Authentication;

public class EmailVerificationService(
    IAccessTokenBlacklist accessTokenBlacklist,
    IAuthSessionService authSessionService,
    IOptions<JwtOptions> jwtOptions,
    WeblyDbContext dbContext) : IEmailVerificationService
{
    public async Task<AuthResult> CompleteAsync(
        UserSecurityToken token,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        token.ConsumedAt = now;
        token.User.EmailVerifiedAt ??= now;
        await dbContext.SaveChangesAsync(cancellationToken);

        // The user's other devices are holding tokens that still say "unverified". Rather than sign
        // those sessions out, mark them stale: the cookie middleware turns a refused token into a
        // silent refresh, so they pick up the truth on their very next request instead of up to an
        // access-token lifetime later.
        accessTokenBlacklist.RevokeUnverifiedTokens(
            token.UserId,
            DateTimeOffset.UtcNow.Add(jwtOptions.Value.AccessTokenLifetime));

        return await authSessionService.IssueAsync(token.User, cancellationToken);
    }
}
