using Microsoft.Extensions.Options;
using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Repositories.RefreshTokens;
using Webly.Services.DTO.Authentication;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Realtime;

namespace Webly.Services.Services.Authentication;

public class AuthSessionService(
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository,
    IAdminPolicy adminPolicy,
    IAccessTokenBlacklist blacklist,
    IRealtimeSessions realtimeSessions,
    IOptions<SitesOptions> sites,
    WeblyDbContext dbContext) : IAuthSessionService
{
    /// <summary>
    /// How long a revoked session is remembered: the longest any credential minted under it can outlive the
    /// revocation. The preview token's twelve hours is that credential today, and an access token's fifteen minutes
    /// sit well inside it. A day rather than the refresh token's sixty, because the entries are held in memory and
    /// remembering a session long after everything it could have minted has expired is paying for nothing.
    /// </summary>
    public static readonly TimeSpan SessionRevocationWindow = TimeSpan.FromDays(1);

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

    public async Task EndAllAsync(int userId, CancellationToken cancellationToken = default)
    {
        var sessions = await refreshTokenRepository.RevokeAllForUserAsync(userId, DateTime.UtcNow, cancellationToken);
        var until = DateTimeOffset.UtcNow.Add(SessionRevocationWindow);

        foreach (var session in sessions)
            blacklist.RevokeSession(session, until);

        // Every socket of the user, not only those of the sessions just named: a connection made with a token that
        // predates the session claim cannot be named at all, and being unnamed is no reason to leave it open.
        realtimeSessions.End(userId, sessionId: null);
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
        CurrentSiteName = user.CurrentSite?.Name,
        MaxSites = sites.Value.MaxSitesPerUser
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
