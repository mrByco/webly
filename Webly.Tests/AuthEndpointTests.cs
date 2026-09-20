using Webly.Api.Infrastructure;
using System.Net;
using System.Text.Json;

namespace Webly.Tests;

public class AuthEndpointTests : AuthEndpointTestBase
{
    private static readonly object Registration = new
    {
        email = "endre@example.com",
        password = "hunyadi-matyas-1458",
        displayName = "Endre"
    };

    private async Task<HttpResponseMessage> RegisterAsync() =>
        await Client.PostAsync("/api/auth/register", Json(Registration));

    [Test]
    public async Task Registering_sets_both_cookies_and_neither_is_readable_by_script()
    {
        var response = await RegisterAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var access = CookieHeader(response, AuthCookies.AccessTokenName);
        var refresh = CookieHeader(response, AuthCookies.RefreshTokenName);

        Assert.That(access, Is.Not.Null);
        Assert.That(refresh, Is.Not.Null);

        foreach (var cookie in new[] { access!, refresh! })
        {
            Assert.That(cookie, Does.Contain("httponly").IgnoreCase);
            Assert.That(cookie, Does.Contain("secure").IgnoreCase);
            Assert.That(cookie, Does.Contain("samesite=lax").IgnoreCase);
        }
    }

    [Test]
    public async Task Me_answers_anonymous_when_signed_out_rather_than_failing()
    {
        var response = await Client.GetAsync("/api/auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.That(body.RootElement.GetProperty("isAuthenticated").GetBoolean(), Is.False);
    }

    [Test]
    public async Task Me_reports_the_signed_in_user_and_that_they_have_no_site_yet()
    {
        var registered = await RegisterAsync();
        var access = CookieValue(registered, AuthCookies.AccessTokenName)!;

        var response = await Client.SendAsync(
            Request(HttpMethod.Get, "/api/auth/me", (AuthCookies.AccessTokenName, access)));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.That(root.GetProperty("isAuthenticated").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("email").GetString(), Is.EqualTo("endre@example.com"));
        Assert.That(root.GetProperty("hasSite").GetBoolean(), Is.False);
    }

    [Test]
    public async Task A_session_survives_on_the_refresh_cookie_alone_and_gets_fresh_cookies_back()
    {
        var registered = await RegisterAsync();
        var refresh = CookieValue(registered, AuthCookies.RefreshTokenName)!;

        // Only the refresh cookie: the browser's access token has expired and been dropped. The
        // middleware should rotate silently — the client never calls a refresh endpoint.
        var response = await Client.SendAsync(
            Request(HttpMethod.Get, "/api/auth/me", (AuthCookies.RefreshTokenName, refresh)));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.That(body.RootElement.GetProperty("isAuthenticated").GetBoolean(), Is.True);
        Assert.That(CookieValue(response, AuthCookies.AccessTokenName), Is.Not.Null);
        Assert.That(CookieValue(response, AuthCookies.RefreshTokenName), Is.Not.EqualTo(refresh),
            "the refresh token must rotate, not be reused");
    }

    [Test]
    public async Task An_endpoint_that_requires_authentication_rejects_an_anonymous_caller()
    {
        var response = await Client.PostAsync("/api/auth/logout", content: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Logging_out_clears_the_cookies_and_kills_the_refresh_token()
    {
        var registered = await RegisterAsync();
        var access = CookieValue(registered, AuthCookies.AccessTokenName)!;
        var refresh = CookieValue(registered, AuthCookies.RefreshTokenName)!;

        var logout = await Client.SendAsync(Request(
            HttpMethod.Post,
            "/api/auth/logout",
            (AuthCookies.AccessTokenName, access),
            (AuthCookies.RefreshTokenName, refresh)));

        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(CookieValue(logout, AuthCookies.AccessTokenName), Is.Null, "the cookie is cleared");

        // The real test: the token itself is dead server-side, so a stolen copy is worthless.
        var replay = await Client.SendAsync(
            Request(HttpMethod.Get, "/api/auth/me", (AuthCookies.RefreshTokenName, refresh)));

        using var body = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());

        Assert.That(body.RootElement.GetProperty("isAuthenticated").GetBoolean(), Is.False);
    }

    [Test]
    public async Task A_replayed_access_token_stops_working_after_logout()
    {
        var registered = await RegisterAsync();
        var access = CookieValue(registered, AuthCookies.AccessTokenName)!;
        var refresh = CookieValue(registered, AuthCookies.RefreshTokenName)!;

        await Client.SendAsync(Request(
            HttpMethod.Post,
            "/api/auth/logout",
            (AuthCookies.AccessTokenName, access),
            (AuthCookies.RefreshTokenName, refresh)));

        var replay = await Client.SendAsync(
            Request(HttpMethod.Get, "/api/auth/me", (AuthCookies.AccessTokenName, access)));

        using var body = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());

        // A signed JWT would normally stay valid until it expired, which is why logout also puts
        // its jti on the in-memory blacklist. Without that, a stolen access cookie would keep
        // working for the rest of the 15-minute window after the user had logged out.
        Assert.That(body.RootElement.GetProperty("isAuthenticated").GetBoolean(), Is.False);
    }

    [Test]
    public async Task Wrong_credentials_are_rejected_and_set_no_cookies()
    {
        await RegisterAsync();

        var response = await Client.PostAsync(
            "/api/auth/login",
            Json(new { email = "endre@example.com", password = "wrong" }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(SetCookies(response), Is.Empty);
    }

    [Test]
    public async Task Google_start_matches_whether_google_is_actually_configured()
    {
        // Deliberately not asserting a fixed answer: whether credentials exist depends on the
        // machine's user secrets, and CI has none. What must always hold is that the advertised
        // capability and the endpoint agree — a button that is offered has to work, and the
        // unconfigured path has to stay healthy rather than 500.
        var providers = await Client.GetAsync("/api/auth/providers");
        using var body = JsonDocument.Parse(await providers.Content.ReadAsStringAsync());
        var enabled = body.RootElement.GetProperty("google").GetBoolean();

        var start = await Client.GetAsync("/api/auth/external/google/start");

        if (enabled)
        {
            Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(start.Headers.Location?.Host, Does.Contain("google.com"));
        }
        else
        {
            Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }

    [Test]
    public async Task Health_and_the_frontend_catch_all_stay_anonymous_despite_default_deny()
    {
        var health = await Client.GetAsync("/health");

        Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // The catch-all proxies to the Angular dev server, which is not running here — so a gateway
        // error is the expected answer. What matters is that it is not 401: if authorization
        // reached the proxy route, nobody could load the login page in order to log in.
        var app = await Client.GetAsync("/login");

        Assert.That(app.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
    }
}
