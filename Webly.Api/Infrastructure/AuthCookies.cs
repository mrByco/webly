namespace Webly.Api.Infrastructure;

/// <summary>
/// The only place auth cookies are written or read. Their names and flags have to match exactly
/// between setting and deleting or the browser keeps a cookie nobody can clear.
/// </summary>
public static class AuthCookies
{
    public const string AccessTokenName = "webly_access";
    public const string RefreshTokenName = "webly_refresh";

    /// <summary>
    /// <c>HttpOnly</c> so script cannot read the tokens even if something injects onto the page,
    /// <c>Secure</c> because the app is HTTPS in development as well as production.
    ///
    /// <c>SameSite=Lax</c>, not <c>Strict</c>, and Google sign-in is why: the browser withholds a
    /// Strict cookie on a navigation that arrives from another site, so a user returning from
    /// Google's consent screen would land back here looking logged out until they reloaded. Lax
    /// sends cookies on top-level GET navigations — exactly this case — while still withholding
    /// them from cross-site POSTs.
    /// </summary>
    private static CookieOptions Options(DateTimeOffset? expires = null) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires
    };

    public static void Set(HttpResponse response, string accessToken, string refreshToken, TimeSpan refreshLifetime)
    {
        // The access cookie is a session cookie: its real lifetime is the `exp` inside the token,
        // and giving the cookie its own expiry only creates a second deadline to disagree with.
        response.Cookies.Append(AccessTokenName, accessToken, Options());
        response.Cookies.Append(RefreshTokenName, refreshToken, Options(DateTimeOffset.UtcNow.Add(refreshLifetime)));
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(AccessTokenName, Options());
        response.Cookies.Delete(RefreshTokenName, Options());
    }

    public static string? GetAccessToken(HttpRequest request) => request.Cookies[AccessTokenName];

    public static string? GetRefreshToken(HttpRequest request) => request.Cookies[RefreshTokenName];
}
