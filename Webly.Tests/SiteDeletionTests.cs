using System.Net;
using System.Text.Json;
using Webly.Api.Infrastructure;

namespace Webly.Tests;

/// <summary>
/// Deleting one site out of several, which is mostly a question about the site that is left.
///
/// <c>User.CurrentSiteId</c> is which site the editor opens on, and the site being deleted is usually the one
/// it points at — so the delete has to choose a successor. The interesting part is *which*: getting it wrong is
/// invisible in a test that only counts rows, and shows up as the app opening on the site its owner has cared
/// about least.
/// </summary>
public class SiteDeletionTests : AuthEndpointTestBase
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

    private async Task<string?> CurrentSiteAsync(string access)
    {
        var me = await Client.SendAsync(Request(HttpMethod.Get, "/api/auth/me", (AuthCookies.AccessTokenName, access)));
        var body = await me.Content.ReadAsStringAsync();

        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);

        var value = JsonDocument.Parse(body).RootElement.GetProperty("currentSiteNanoid");

        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    [Test]
    public async Task Deleting_the_open_site_opens_the_one_touched_most_recently()
    {
        var owner = await AccountAsync("delete-successor@example.com");

        var first = await SiteAsync(owner, "The first one");
        var second = await SiteAsync(owner, "The second one");
        var third = await SiteAsync(owner, "The third one");

        // Creating a site makes it the current one, so the third is what the editor is on.
        Assert.That(await CurrentSiteAsync(owner), Is.EqualTo(third));

        var deleted = await Client.SendAsync(
            Request(HttpMethod.Delete, $"/api/sites/{third}", (AuthCookies.AccessTokenName, owner)));

        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await deleted.Content.ReadAsStringAsync());

        // The second, not the first: sites are listed most-recently-updated first, and the successor should be
        // the one its owner was working on before this one — which is what makes the next visit land somewhere
        // they recognise.
        Assert.That(await CurrentSiteAsync(owner), Is.EqualTo(second),
            "the successor should be the most recently updated remaining site, not the least");

        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public async Task Deleting_the_last_site_leaves_the_account_without_one()
    {
        var owner = await AccountAsync("delete-only@example.com");
        var only = await SiteAsync(owner, "The only one");

        await Client.SendAsync(Request(HttpMethod.Delete, $"/api/sites/{only}", (AuthCookies.AccessTokenName, owner)));

        // Null rather than a dangling id, so the app offers to create one exactly as it does for a new account.
        Assert.That(await CurrentSiteAsync(owner), Is.Null);
    }

    [Test]
    public async Task Deleting_a_site_that_is_not_the_open_one_leaves_the_pointer_alone()
    {
        var owner = await AccountAsync("delete-other@example.com");

        var kept = await SiteAsync(owner, "The one being looked at");
        var spare = await SiteAsync(owner, "The spare");

        // Switch back to the first, so the site about to go is not the current one.
        await Client.SendAsync(Request(HttpMethod.Post, $"/api/sites/{kept}/open", (AuthCookies.AccessTokenName, owner)));

        Assert.That(await CurrentSiteAsync(owner), Is.EqualTo(kept));

        await Client.SendAsync(Request(HttpMethod.Delete, $"/api/sites/{spare}", (AuthCookies.AccessTokenName, owner)));

        Assert.That(await CurrentSiteAsync(owner), Is.EqualTo(kept),
            "deleting another site should not move what the editor is open on");
    }
}
