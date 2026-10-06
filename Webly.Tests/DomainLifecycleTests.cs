using System.Net;
using System.Text.Json;
using Webly.Api.Infrastructure;

namespace Webly.Tests;

/// <summary>
/// Connecting a domain and disconnecting it again, over HTTP, in the order somebody does it.
///
/// <b>The last step used to be impossible.</b> Removing the site's main address was refused with "make
/// another domain the main one first" — which nobody with one domain can do, and one domain is what every
/// site that has ever connected one has. The button was hidden on that row, so the advice behind it was
/// unreachable as well as impossible: a domain somebody added could never be taken away.
///
/// The development deployment target simulates the provider — attach answers pending with a DNS record,
/// check answers verified — which is exactly the sequence this walks.
/// </summary>
public class DomainLifecycleTests : AuthEndpointTestBase
{
    private const string Password = "hunyadi-matyas-1458";

    private async Task<string> AccountAsync(string email)
    {
        await Client.PostAsync("/api/auth/register",
            Json(new { email, password = Password, displayName = email.Split('@')[0] }));

        var verified = await Client.PostAsync("/api/auth/email/verify", Json(new { token = LatestTokenFor(email) }));

        return CookieValue(verified, AuthCookies.AccessTokenName)!;
    }

    private async Task<JsonElement> SendAsync(string access, HttpMethod method, string path, object? body = null)
    {
        var request = Request(method, path, (AuthCookies.AccessTokenName, access));

        if (body is not null) request.Content = Json(body);

        var response = await Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        Assert.That(response.IsSuccessStatusCode, Is.True, $"{method} {path} → {response.StatusCode}: {text}");

        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    [Test]
    public async Task A_domain_can_be_connected_promoted_and_disconnected_again()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = (await SendAsync(owner, HttpMethod.Post, "/api/sites", new { name = "Koopman Cycles" }))
            .GetProperty("nanoid").GetString()!;

        var weblyUrl = (await SendAsync(owner, HttpMethod.Get, $"/api/sites/{site}"))
            .GetProperty("summary").GetProperty("weblyUrl").GetString();

        var added = await SendAsync(owner, HttpMethod.Post, $"/api/sites/{site}/domains",
            new { hostname = "koopmancycles.nl" });

        Assert.Multiple(() =>
        {
            Assert.That(added.GetProperty("verificationState").GetString(), Is.EqualTo("Pending"));

            // The record the owner has to reproduce in their registrar. All three parts, because a screen that
            // shows two of them is a screen somebody cannot finish.
            Assert.That(added.GetProperty("dnsRecordType").GetString(), Is.Not.Empty);
            Assert.That(added.GetProperty("dnsRecordName").GetString(), Is.Not.Empty);
            Assert.That(added.GetProperty("dnsRecordValue").GetString(), Is.Not.Empty);
        });

        var domain = added.GetProperty("nanoid").GetString()!;

        // Until it is verified it is not the address, and it cannot be made the main one.
        var early = await Client.SendAsync(
            Request(HttpMethod.Post, $"/api/sites/{site}/domains/{domain}/primary", (AuthCookies.AccessTokenName, owner)));

        Assert.That(early.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var checked_ = await SendAsync(owner, HttpMethod.Post, $"/api/sites/{site}/domains/{domain}/check");

        Assert.That(checked_.GetProperty("verificationState").GetString(), Is.EqualTo("Verified"));

        await SendAsync(owner, HttpMethod.Post, $"/api/sites/{site}/domains/{domain}/primary");

        var withDomain = await SendAsync(owner, HttpMethod.Get, $"/api/sites/{site}");

        Assert.That(withDomain.GetProperty("summary").GetProperty("url").GetString(),
            Is.EqualTo("https://koopmancycles.nl"), "the address follows the main domain");

        // And back again, which is the step that used to be refused.
        var removed = await Client.SendAsync(
            Request(HttpMethod.Delete, $"/api/sites/{site}/domains/{domain}", (AuthCookies.AccessTokenName, owner)));

        Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var after = await SendAsync(owner, HttpMethod.Get, $"/api/sites/{site}");

        Assert.Multiple(() =>
        {
            Assert.That(after.GetProperty("summary").GetProperty("url").GetString(), Is.EqualTo(weblyUrl),
                "the address goes back to the Webly subdomain, which was there all along");
            Assert.That(after.GetProperty("domains").GetArrayLength(), Is.Zero);
        });
    }

    /// <summary>
    /// Webly's own zone is refused whichever way it is typed, and nothing is attached or stored for it: the provider
    /// already serves that zone for this account, so an attach would have been live at once.
    /// </summary>
    [Test]
    public async Task An_address_in_the_zone_Webly_gives_out_cannot_be_connected()
    {
        var owner = await AccountAsync("squatter@example.com");
        var site = (await SendAsync(owner, HttpMethod.Post, "/api/sites", new { name = "Ridgeway Cycles" }))
            .GetProperty("nanoid").GetString()!;

        foreach (var hostname in new[] { "brightwater-florist.webly.site", "https://WEBLY.SITE./", "www.webly.site" })
        {
            var request = Request(HttpMethod.Post, $"/api/sites/{site}/domains", (AuthCookies.AccessTokenName, owner));
            request.Content = Json(new { hostname });

            var response = await Client.SendAsync(request);
            var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), hostname);
                Assert.That(problem.GetProperty("title").GetString(), Is.EqualTo("That address is one of Webly's."));
            });
        }

        var domains = (await SendAsync(owner, HttpMethod.Get, $"/api/sites/{site}")).GetProperty("domains");

        Assert.That(domains.GetArrayLength(), Is.Zero, "nothing was stored for any of them");
    }
}
