using System.Net;
using System.Text.Json;
using Webly.Api.Infrastructure;

namespace Webly.Tests;

/// <summary>
/// How many sites an account can have. Each one is a git repository on our disk and a subdomain nobody else can
/// then have, so the limit is about more than tidiness — and it is a count, which is a limit only if it holds when
/// the creations arrive together.
/// </summary>
public class SiteLimitTests : AuthEndpointTestBase
{
    private const int Limit = 3;

    private async Task<string> AccountAsync(string email)
    {
        await Client.PostAsync("/api/auth/register",
            Json(new { email, password = "szent-istvan-1000", displayName = email.Split('@')[0] }));

        var verified = await Client.PostAsync("/api/auth/email/verify", Json(new { token = LatestTokenFor(email) }));

        return CookieValue(verified, AuthCookies.AccessTokenName)!;
    }

    private Task<HttpResponseMessage> CreateAsync(string access, string name)
    {
        var request = Request(HttpMethod.Post, "/api/sites", (AuthCookies.AccessTokenName, access));
        request.Content = Json(new { name });

        return Client.SendAsync(request);
    }

    private async Task<int> CountAsync(string access)
    {
        var listed = await Client.SendAsync(Request(HttpMethod.Get, "/api/sites", (AuthCookies.AccessTokenName, access)));

        return JsonDocument.Parse(await listed.Content.ReadAsStringAsync()).RootElement.GetArrayLength();
    }

    [Test]
    public async Task The_site_after_the_limit_is_refused()
    {
        var owner = await AccountAsync("one-at-a-time@example.com");

        for (var i = 0; i < Limit; i++)
            Assert.That((await CreateAsync(owner, $"Site {i}")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That((await CreateAsync(owner, "One too many")).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await CountAsync(owner), Is.EqualTo(Limit));
    }

    [Test]
    public async Task Creations_sent_at_once_stop_at_the_limit()
    {
        var owner = await AccountAsync("all-at-once@example.com");

        // The burst that got through: every creation read the count before any of them had written a site, so ten
        // at once made ten. Live, twenty made twenty on an account allowed three.
        var answers = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => CreateAsync(owner, $"Burst {i}")));

        Assert.That(answers.Count(x => x.StatusCode == HttpStatusCode.OK), Is.EqualTo(Limit));
        Assert.That(answers.Count(x => x.StatusCode == HttpStatusCode.Conflict), Is.EqualTo(10 - Limit));
        Assert.That(await CountAsync(owner), Is.EqualTo(Limit));
    }
}
