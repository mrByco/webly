using Webly.Api.Infrastructure;
using Webly.Services.Services.Authentication;
using Webly.Services.UseCases.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Webly.Api.Middleware;

/// <summary>
/// Translates the auth cookies into the <c>Authorization</c> header the JWT bearer handler expects,
/// and silently refreshes an expired session on the way through.
///
/// This is why there is no refresh endpoint: the client never learns that access tokens expire. A
/// request arriving with a dead access cookie and a live refresh cookie is rotated here and
/// continues as a normal authenticated request, with fresh cookies on the response.
///
/// Must be registered before <c>UseAuthentication()</c> — after it, the header arrives too late to
/// be read. Unlike the reference implementation this never creates a user: <b>not authenticating is
/// a valid outcome</b>, and unauthenticated requests fall through to the authorization policy.
/// </summary>
public class CookieAuthenticationMiddleware(
    RequestDelegate next,
    IOptions<JwtOptions> jwtOptions)
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    public async Task InvokeAsync(HttpContext context)
    {
        // A caller that brought its own bearer token is left alone. Nothing in the app does this
        // today, but silently overwriting a caller's credentials would be a baffling bug when
        // something eventually does.
        if (!context.Request.Headers.ContainsKey("Authorization"))
            await ApplyCookieAsync(context);

        await next(context);
    }

    private async Task ApplyCookieAsync(HttpContext context)
    {
        var accessToken = AuthCookies.GetAccessToken(context.Request);

        if (accessToken is not null && IsUsable(context, accessToken))
        {
            InjectHeader(context, accessToken);
            return;
        }

        var refreshToken = AuthCookies.GetRefreshToken(context.Request);

        if (refreshToken is null)
            return;

        var rotate = context.RequestServices.GetRequiredService<RotateRefreshToken>();
        var result = await rotate.Execute(refreshToken, context.RequestAborted);

        if (!result.Succeeded)
        {
            // The refresh token is spent, expired or forged. Clear both cookies so the browser
            // stops presenting them on every subsequent request.
            AuthCookies.Clear(context.Response);
            return;
        }

        AuthCookies.Set(
            context.Response,
            result.AccessToken!,
            result.RefreshToken!,
            jwtOptions.Value.RefreshTokenLifetime);

        InjectHeader(context, result.AccessToken!);
    }

    /// <summary>
    /// Whether this access token can be used as-is, or whether we should fall through and refresh.
    ///
    /// Reads the token without validating its signature — deliberately. This only decides whether to
    /// attempt a refresh; real validation happens in the bearer handler a moment later, and a forged
    /// token that survives this check still fails there.
    ///
    /// Revoked tokens are treated as unusable rather than passed on to be rejected, so that a token
    /// which has merely gone <i>stale</i> — the user verified their email elsewhere, so the claims
    /// in it are out of date — silently refreshes into a truthful one instead of 401-ing. Logout is
    /// unaffected: its refresh token is revoked too, so the fall-through fails and the session ends,
    /// which is the intent there.
    /// </summary>
    private static bool IsUsable(HttpContext context, string token)
    {
        try
        {
            var parsed = TokenHandler.ReadJsonWebToken(token);

            if (parsed.ValidTo <= DateTime.UtcNow)
                return false;

            if (!int.TryParse(parsed.GetClaim(JwtTokenService.UserIdClaim)?.Value, out var userId))
                return false;

            var emailVerified =
                bool.TryParse(parsed.GetClaim(JwtTokenService.EmailVerifiedClaim)?.Value, out var flag) && flag;

            var blacklist = context.RequestServices.GetRequiredService<IAccessTokenBlacklist>();

            return !blacklist.IsRevoked(parsed.Id, userId, emailVerified);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // GetClaim throws when the claim is absent — a token shaped unlike ours.
            return false;
        }
    }

    private static void InjectHeader(HttpContext context, string token) =>
        context.Request.Headers.Authorization = $"Bearer {token}";
}
