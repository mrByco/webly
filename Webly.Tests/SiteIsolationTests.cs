using System.Net;
using System.Text.Json;
using Webly.Api.Infrastructure;

namespace Webly.Tests;

/// <summary>
/// Somebody else's site, asked for by an account that does not own it.
///
/// Every surface under <c>/api/sites/{nanoid}</c> has to answer <b>404, never 403</b>: a 403 confirms that a
/// guessed nanoid names a real site, which is the one thing a stranger should not be able to learn. The rule is
/// written once, in <c>ISiteRepository.FindForOwnerAsync</c> and <c>SiteController.Failure</c> — and a rule with
/// one implementation and fifteen callers is exactly the thing that stays correct until somebody adds a
/// sixteenth. This test is the sixteenth caller's alarm.
///
/// It drives the real HTTP surface rather than the use cases, because the interesting failure is a new endpoint
/// that forgets to start from <c>FindForOwnerAsync</c>, and no unit test of the old ones would notice.
/// </summary>
public class SiteIsolationTests : AuthEndpointTestBase
{
    private const string Password = "hunyadi-matyas-1458";

    /// <summary>Registers, verifies from the mailed link, and hands back the access cookie.</summary>
    private async Task<string> AccountAsync(string email)
    {
        var registered = await Client.PostAsync("/api/auth/register",
            Json(new { email, password = Password, displayName = email.Split('@')[0] }));

        Assert.That(registered.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"registering {email}");

        var verified = await Client.PostAsync("/api/auth/email/verify",
            Json(new { token = LatestTokenFor(email) }));

        Assert.That(verified.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"verifying {email}");

        return CookieValue(verified, AuthCookies.AccessTokenName)!;
    }

    private async Task<HttpResponseMessage> AsAsync(string access, HttpMethod method, string url, object? body = null)
    {
        var request = Request(method, url, (AuthCookies.AccessTokenName, access));

        if (body is not null) request.Content = Json(body);

        return await Client.SendAsync(request);
    }

    [Test]
    public async Task A_stranger_cannot_tell_that_somebody_else_is_site_exists()
    {
        var owner = await AccountAsync("owner@example.com");
        var stranger = await AccountAsync("stranger@example.com");

        var created = await AsAsync(owner, HttpMethod.Post, "/api/sites", new { name = "Koopman Cycles" });

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());

        var site = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("nanoid").GetString()!;

        // Everything the site's own routes offer, read and write. Listed rather than discovered: a route that
        // this test does not name is a route nobody checked, and that is worth being able to see.
        var surfaces = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, $"/api/sites/{site}", null),
            (HttpMethod.Get, $"/api/sites/{site}/versions", null),
            (HttpMethod.Get, $"/api/sites/{site}/files", null),
            (HttpMethod.Get, $"/api/sites/{site}/file?path=src/app/page.tsx", null),
            (HttpMethod.Get, $"/api/sites/{site}/export", null),
            (HttpMethod.Get, $"/api/sites/{site}/chat", null),
            (HttpMethod.Get, $"/api/sites/{site}/deployments", null),
            (HttpMethod.Get, $"/api/sites/{site}/domains", null),
            (HttpMethod.Get, $"/api/sites/{site}/submissions", null),
            (HttpMethod.Post, $"/api/sites/{site}/submissions/read", null),
            (HttpMethod.Post, $"/api/sites/{site}/preview-access", null),
            (HttpMethod.Delete, $"/api/sites/{site}/submissions/made-up-nanoid", null),
            (HttpMethod.Get, $"/api/sites/{site}/preview/", null),
            (HttpMethod.Post, $"/api/sites/{site}/workspace", null),
            (HttpMethod.Post, $"/api/sites/{site}/open", null),
            (HttpMethod.Post, $"/api/sites/{site}/deployments", new { }),
            (HttpMethod.Post, $"/api/sites/{site}/domains", new { hostname = "stolen.example" }),
            (HttpMethod.Put, $"/api/sites/{site}", new { name = "Mine now" }),
            (HttpMethod.Delete, $"/api/sites/{site}", null),
        };

        // Collected and asserted at the end rather than one Assert.That per request: the useful output is
        // every surface that leaks, not the first one. (`Assert.Multiple` with an async body does not await it,
        // which is its own trap.)
        var leaks = new List<string>();

        foreach (var (method, url, body) in surfaces)
        {
            var response = await AsAsync(stranger, method, url, body);

            if (response.StatusCode != HttpStatusCode.NotFound)
                leaks.Add($"{method} {url} → {(int)response.StatusCode}");
        }

        Assert.That(leaks, Is.Empty, "every one of these must answer 404 to somebody who does not own the site");

        // `chat/status` is the one route under a site that answers a stranger, and it is deliberately not on the
        // list: it reports whether *this deployment* has an agent at all and never looks at the site. The proof
        // that this is not a leak is that a nanoid nobody has ever owned gets the same answer — so holding a real
        // one tells you nothing.
        var real = await AsAsync(stranger, HttpMethod.Get, $"/api/sites/{site}/chat/status");
        var invented = await AsAsync(stranger, HttpMethod.Get, "/api/sites/not-a-site-at-all/chat/status");

        Assert.That(real.StatusCode, Is.EqualTo(invented.StatusCode));
        Assert.That(await real.Content.ReadAsStringAsync(), Is.EqualTo(await invented.Content.ReadAsStringAsync()));

        // And the owner still has it, which is what makes the 404s above mean "not yours" rather than "gone".
        var mine = await AsAsync(owner, HttpMethod.Get, $"/api/sites/{site}");

        Assert.That(mine.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task A_site_list_holds_only_the_caller_is_own_sites()
    {
        var owner = await AccountAsync("owner@example.com");
        var stranger = await AccountAsync("stranger@example.com");

        await AsAsync(owner, HttpMethod.Post, "/api/sites", new { name = "Koopman Cycles" });

        var theirs = await AsAsync(stranger, HttpMethod.Get, "/api/sites");
        var list = JsonDocument.Parse(await theirs.Content.ReadAsStringAsync()).RootElement;

        Assert.That(list.GetArrayLength(), Is.Zero);
    }
}
