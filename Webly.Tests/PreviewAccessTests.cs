using System.Net;
using System.Text.Json;
using Webly.Api.Infrastructure;

namespace Webly.Tests;

/// <summary>
/// Who may reach a site's preview, and with what.
///
/// <b>This exists because of what the preview used to be.</b> The frame was same-origin with the app, and
/// that was written down as the thing that made it safe. It was the thing that made it dangerous: the page
/// inside it is the customer's own website, written by a coding agent, and a script in it could call
/// <c>/api/sites</c> or <c>DELETE /api/auth/account</c> with the owner's session and be indistinguishable from
/// the app doing it. Confirmed by putting a <c>fetch</c> in a site's home page and watching the preview print
/// the owner's sites back; refused, from the same page, once the frame was sandboxed.
///
/// The frame having no origin of Webly's is what makes that true, and it is a line of markup this suite cannot
/// see. What it can check is the half that had to change underneath: the preview stopped being able to use the
/// session cookie, so it has its own credential — and a credential is only worth having if it is checked.
/// </summary>
public class PreviewAccessTests : AuthEndpointTestBase
{
    private const string Password = "hunyadi-matyas-1458";

    private async Task<string> AccountAsync(string email)
    {
        await Client.PostAsync("/api/auth/register",
            Json(new { email, password = Password, displayName = email.Split('@')[0] }));

        var verified = await Client.PostAsync("/api/auth/email/verify", Json(new { token = LatestTokenFor(email) }));

        return CookieValue(verified, AuthCookies.AccessTokenName)!;
    }

    private async Task<string> SiteAsync(string access, string name)
    {
        var request = Request(HttpMethod.Post, "/api/sites", (AuthCookies.AccessTokenName, access));
        request.Content = Json(new { name });

        var created = await Client.SendAsync(request);

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());

        return JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("nanoid").GetString()!;
    }

    private async Task<string> PreviewCookieAsync(string access, string site)
    {
        var response = await Client.SendAsync(Request(
            HttpMethod.Post, $"/api/sites/{site}/preview-access", (AuthCookies.AccessTokenName, access)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        return CookieValue(response, PreviewAccess.CookieName)!;
    }

    [Test]
    public async Task A_preview_credential_is_issued_for_a_site_you_own_and_scoped_to_it()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        var response = await Client.SendAsync(Request(
            HttpMethod.Post, $"/api/sites/{site}/preview-access", (AuthCookies.AccessTokenName, owner)));

        var cookie = SetCookies(response).First(x => x.StartsWith($"{PreviewAccess.CookieName}=", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

            // The three flags that make it work at all, each for its own reason: the path so one site's
            // credential is not sent for another's, `None` so an opaque origin still sends it, `Secure`
            // because a browser refuses `None` without it — and silently, which is the one way this fails
            // where nobody notices.
            Assert.That(cookie, Does.Contain($"path={PreviewAccess.PathFor(site)}").IgnoreCase);
            Assert.That(cookie, Does.Contain("samesite=none").IgnoreCase);
            Assert.That(cookie, Does.Contain("secure").IgnoreCase);
            Assert.That(cookie, Does.Contain("httponly").IgnoreCase);
        });
    }

    [Test]
    public async Task A_stranger_cannot_get_a_credential_for_somebody_else_is_site()
    {
        var owner = await AccountAsync("owner@example.com");
        var stranger = await AccountAsync("stranger@example.com");

        var site = await SiteAsync(owner, "Koopman Cycles");

        var response = await Client.SendAsync(Request(
            HttpMethod.Post, $"/api/sites/{site}/preview-access", (AuthCookies.AccessTokenName, stranger)));

        // 404, the rule every per-site route follows: a 403 would confirm the nanoid names a real site.
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(CookieValue(response, PreviewAccess.CookieName), Is.Null);
        });
    }

    /// <summary>
    /// The preview route is the one <c>[AllowAnonymous]</c> under a site, because a sandboxed frame sends no
    /// session cookie. So the thing to prove is that anonymous means "with the preview credential", not
    /// "with nothing".
    /// </summary>
    [Test]
    public async Task The_preview_is_not_open_to_anybody_who_asks()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        var anonymous = await Client.GetAsync($"/api/sites/{site}/preview/");

        Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "no credential at all");
    }

    [Test]
    public async Task One_site_is_credential_does_not_open_another_site_is_preview()
    {
        var owner = await AccountAsync("owner@example.com");

        var mine = await SiteAsync(owner, "Koopman Cycles");
        var other = await SiteAsync(owner, "Koopman Coffee");

        var cookie = await PreviewCookieAsync(owner, mine);

        // Presented against the other site — which the same person owns, so this is not about ownership. The
        // token names one site, and a credential that is good for "any site of yours" is one a page in one
        // site can carry into another.
        var response = await Client.SendAsync(Request(
            HttpMethod.Get, $"/api/sites/{other}/preview/", (PreviewAccess.CookieName, cookie)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// The preview token used to outlive the session that minted it — for twelve hours, on whatever machine
    /// the browser was left on. It is narrow (one site's preview and nothing else) and it is real: a site
    /// nobody has published is not otherwise readable, and a shared computer is exactly where somebody signs
    /// out. It could not be fixed until a session had a name to put in the token.
    /// </summary>
    [Test]
    public async Task Signing_out_ends_the_preview_credential_that_session_minted()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");
        var cookie = await PreviewCookieAsync(owner, site);

        // It works first, or the rest of this proves nothing.
        var before = await Client.SendAsync(Request(
            HttpMethod.Get, $"/api/sites/{site}/preview/", (PreviewAccess.CookieName, cookie)));

        Assert.That(before.StatusCode, Is.Not.EqualTo(HttpStatusCode.NotFound), "the credential should be good");

        await Client.SendAsync(Request(HttpMethod.Post, "/api/auth/logout", (AuthCookies.AccessTokenName, owner)));

        var after = await Client.SendAsync(Request(
            HttpMethod.Get, $"/api/sites/{site}/preview/", (PreviewAccess.CookieName, cookie)));

        // 404, the answer every per-site route gives to somebody it does not recognise.
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_forged_credential_is_not_a_credential()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        var response = await Client.SendAsync(Request(
            HttpMethod.Get, $"/api/sites/{site}/preview/", (PreviewAccess.CookieName, "not-a-real-token")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// The one shape of request served without a credential, and the reason it has to be: a font is always
    /// fetched with CORS in credentials mode <c>same-origin</c>, and nothing is same-origin to an opaque
    /// origin, so a sandboxed frame can never attach a cookie to one. Everything that is not exactly this —
    /// another directory, another extension, another method — still takes the credential.
    /// </summary>
    [Test]
    public async Task A_font_is_the_only_thing_the_preview_serves_without_a_credential()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        // 503 rather than 404: no credential, and it still got past the check to the workspace it has not got.
        var font = await Client.GetAsync($"/api/sites/{site}/preview/_next/static/media/abc123-s.p.woff2");

        Assert.Multiple(() =>
        {
            Assert.That(font.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));

            // Without this the browser blocks it even when the bytes arrive, which is the whole failure.
            Assert.That(font.Headers.TryGetValues("Access-Control-Allow-Origin", out var allow), Is.True);
            Assert.That(allow!.First(), Is.EqualTo("*"));
        });

        // The neighbours, each a 404 because each still needs the credential.
        Assert.Multiple(async () =>
        {
            foreach (var path in new[]
            {
                "_next/static/media/chunk.js",          // same directory, not a font
                "_next/static/chunks/main-app.woff2",   // font extension, wrong directory
                "_next/static/media/../../../secret",   // the directory check is not a prefix to walk out of
                ""                                      // the page itself
            })
            {
                var response = await Client.GetAsync($"/api/sites/{site}/preview/{path}");

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), path);
            }
        });
    }

    /// <summary>
    /// With the right credential the route gets past the ownership check and reaches the workspace — which in
    /// a test has none, so the honest answer is "your preview is not running" rather than "no such site". That
    /// difference is the whole assertion: 503 means the credential was accepted.
    /// </summary>
    [Test]
    public async Task The_right_credential_reaches_the_preview()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        var cookie = await PreviewCookieAsync(owner, site);

        var response = await Client.SendAsync(Request(
            HttpMethod.Get, $"/api/sites/{site}/preview/", (PreviewAccess.CookieName, cookie)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }
}
