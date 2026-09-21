using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Repositories.RefreshTokens;
using Webly.Services.DTO.Authentication;

namespace Webly.Services.Services.Authentication;

public class AuthSessionService(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IAdminPolicy adminPolicy,
    WeblyDbContext dbContext) : IAuthSessionService
{
    public async Task<AuthResult> IssueAsync(
        User user,
        string? continuingSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var (rawRefreshToken, row) = tokenService.CreateRefreshToken(user, continuingSessionId);
        refreshTokenRepository.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Minted after the save: a brand-new user has no Id or Nanoid until then, and both go into
        // the token.
        var accessToken = tokenService.CreateAccessToken(user, row.SessionId);

        return AuthResult.Success(accessToken, rawRefreshToken, Describe(user));
    }

    public MeResponse Describe(User user) => new()
    {
        IsAuthenticated = true,
        Nanoid = user.Nanoid,
        Email = user.Email,
        DisplayName = user.DisplayName,
        ProfilePictureUrl = user.ProfilePictureUrl,
        Roles = DescribeRoles(user),
        HasPassword = !string.IsNullOrEmpty(user.PasswordHash),
        EmailVerified = user.EmailVerifiedAt is not null,
        LinkedProviders = [.. user.ExternalLogins.Select(x => x.Provider.ToString())],
        HasSite = user.CurrentSiteId is not null,
        CurrentSiteNanoid = user.CurrentSite?.Nanoid,
        CurrentSiteName = user.CurrentSite?.Name
    };

    /// <summary>
    /// The stored roles, plus <see cref="UserRoles.Admin"/> when configuration says this address
    /// administers the platform. Folded in here so <c>Roles</c> on the profile stays the
    /// client's single source of truth and nothing has to ask a second endpoint.
    /// </summary>
    private IReadOnlyList<string> DescribeRoles(User user)
    {
        if (!adminPolicy.IsAdmin(user.Email) || user.Roles.Contains(UserRoles.Admin))
            return [.. user.Roles];

        return [.. user.Roles, UserRoles.Admin];
    }
}
