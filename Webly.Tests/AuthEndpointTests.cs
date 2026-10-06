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

    /// <summary>
    /// The rest of a batch, through the middleware rather than the use case. When the access token dies a page's
    /// parallel requests all carry the same refresh cookie; one rotates it and the others land inside
    /// <c>RotateRefreshToken.ReuseGrace</c>, which answers with an access token and deliberately no refresh
    /// token. The use case was tested for that and the middleware was not, and it wrote the missing refresh
    /// token into a cookie with a null-forgiving <c>!</c> — so every request but one answered 500, on any page
    /// that loads more than one thing, every fifteen minutes. Found by a script whose cookie jar lagged a
    /// rotation behind, which is the same shape.
    /// </summary>
    [Test]
    public async Task The_rest_of_a_batch_is_served_and_leaves_the_refresh_cookie_alone()
    {
        var registered = await RegisterAsync();
        var refresh = CookieValue(registered, AuthCookies.RefreshTokenName)!;

        var first = await Client.SendAsync(
            Request(HttpMethod.Get, "/api/auth/me", (AuthCookies.RefreshTokenName, refresh)));
        var sibling = await Client.SendAsync(
            Request(HttpMethod.Get, "/api/auth/me", (AuthCookies.RefreshTokenName, refresh)));

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sibling.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the sibling is served, not a 500");
            Assert.That(CookieValue(sibling, AuthCookies.AccessTokenName), Is.Not.Null, "with an access cookie");
            Assert.That(
                CookieHeader(sibling, AuthCookies.RefreshTokenName), Is.Null,
                "and no word about the refresh cookie, so the successor its sibling set is the one the browser keeps");
        });
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

    /// <summary>
    /// A password reset ends every other session at once, not only their refresh tokens. The other browser's access
    /// cookie used to go on answering 200 for the rest of its fifteen minutes — its preview cookie for twelve hours,
    /// its hub socket for as long as it stayed open — after somebody reset their password because they suspected
    /// that browser was not theirs.
    /// </summary>
    [Test]
    public async Task A_password_reset_ends_the_other_sessions_at_once()
    {
        await RegisterAsync();
        await Client.PostAsync("/api/auth/email/verify", Json(new { token = LatestTokenFor("endre@example.com") }));

        var other = await Client.PostAsync(
            "/api/auth/login",
            Json(new { email = "endre@example.com", password = "hunyadi-matyas-1458" }));
        var otherAccess = CookieValue(other, AuthCookies.AccessTokenName)!;

        var before = await Client.SendAsync(Request(HttpMethod.Get, "/api/sites", (AuthCookies.AccessTokenName, otherAccess)));

        await Client.PostAsync("/api/auth/password/forgot", Json(new { email = "endre@example.com" }));
        var reset = await Client.PostAsync(
            "/api/auth/password/reset",
            Json(new { token = LatestTokenFor("endre@example.com"), newPassword = "a-new-password-2026" }));

        var after = await Client.SendAsync(Request(HttpMethod.Get, "/api/sites", (AuthCookies.AccessTokenName, otherAccess)));
        var resetter = await Client.SendAsync(Request(
            HttpMethod.Get,
            "/api/sites",
            (AuthCookies.AccessTokenName, CookieValue(reset, AuthCookies.AccessTokenName)!)));

        Assert.Multiple(() =>
        {
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the other browser was signed in");
            Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "and is not, the moment the reset lands");
            Assert.That(resetter.StatusCode, Is.EqualTo(HttpStatusCode.OK), "while the session the reset began works");
        });
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

    /// <summary>
    /// A name can be corrected. There was no way to, and a typo from the first minute of signing up was in every email,
    /// the sidebar and every version's author for good.
    /// </summary>
    [Test]
    public async Task A_name_can_be_corrected_and_is_stored_trimmed()
    {
        var registered = await RegisterAsync();
        var access = CookieValue(registered, AuthCookies.AccessTokenName)!;

        var change = Request(HttpMethod.Put, "/api/auth/me", (AuthCookies.AccessTokenName, access));
        change.Content = Json(new { displayName = "  Ada Lovelace  " });
        var changed = await Client.SendAsync(change);

        var me = await Client.SendAsync(Request(HttpMethod.Get, "/api/auth/me", (AuthCookies.AccessTokenName, access)));
        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.RootElement.GetProperty("displayName").GetString(), Is.EqualTo("Ada Lovelace"));
        });
    }

    [Test]
    public async Task A_name_of_nothing_but_spaces_is_refused()
    {
        var registered = await RegisterAsync();
        var access = CookieValue(registered, AuthCookies.AccessTokenName)!;

        var change = Request(HttpMethod.Put, "/api/auth/me", (AuthCookies.AccessTokenName, access));
        change.Content = Json(new { displayName = "   " });

        Assert.That((await Client.SendAsync(change)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Changing_a_name_needs_somebody_signed_in()
    {
        var response = await Client.PutAsync("/api/auth/me", Json(new { displayName = "Nobody" }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
