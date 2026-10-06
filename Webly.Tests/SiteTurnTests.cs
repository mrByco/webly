using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Webly.Api.Infrastructure;
using Webly.Data;
using Webly.Data.Repositories.Chat;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services.Realtime;

namespace Webly.Tests;

/// <summary>
/// The changes that reach a site's source from outside a turn — bringing a version back, writing a new name into the
/// pages, adding or removing a photograph — while a turn is running.
///
/// Each used to commit underneath the turn and then wait on it: the request hung for the rest of the turn, and the
/// turn's own commit was refused at the end, throwing its work away. Driven in the running app before these were
/// written. They refuse now, at once, and a turn is simulated here as the running app sees one — a chat run in the
/// registry, correlated to the site's open thread — because what is under test is the refusal, not the agent.
/// </summary>
public class SiteTurnTests : AuthEndpointTestBase
{
    private const string Password = "kinizsi-pal-1494";

    private static byte[] Gif() => Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

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

        return JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("nanoid").GetString()!;
    }

    /// <summary>A turn running on the site, as the registry knows one; finished again by disposing what this returns.</summary>
    private async Task<IDisposable> TurnAsync(string siteNanoid)
    {
        using var scope = Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<WeblyDbContext>();
        var site = await db.Sites.SingleAsync(x => x.Nanoid == siteNanoid);
        var conversation = await scope.ServiceProvider.GetRequiredService<IConversationRepository>()
            .FindOrCreateActiveAsync(site.Id, site.OwnerId, "A turn");

        var runs = Services.GetRequiredService<RunRegistry>();
        var runId = $"run_{Guid.NewGuid():N}";

        runs.Register(RunKind.Chat, runId, conversation.Nanoid, site.OwnerId);

        return new Finisher(() => runs.Finish(runId));
    }

    private sealed class Finisher(Action finish) : IDisposable
    {
        public void Dispose() => finish();
    }

    private async Task<JsonElement> VersionsAsync(string access, string site)
    {
        var listed = await Client.SendAsync(
            Request(HttpMethod.Get, $"/api/sites/{site}/versions", (AuthCookies.AccessTokenName, access)));

        return JsonDocument.Parse(await listed.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private Task<HttpResponseMessage> RenameAsync(string access, string site, string name, bool applyToSite)
    {
        var request = Request(HttpMethod.Put, $"/api/sites/{site}", (AuthCookies.AccessTokenName, access));
        request.Content = Json(new { name, applyToSite });

        return Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> UploadAsync(string access, string site, string fileName)
    {
        var part = new ByteArrayContent(Gif());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var request = Request(HttpMethod.Post, $"/api/sites/{site}/images", (AuthCookies.AccessTokenName, access));
        request.Content = new MultipartFormDataContent { { part, "files", fileName } };

        return Client.SendAsync(request);
    }

    private static async Task AssertRefusedForTheTurnAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), body);
        Assert.That(
            JsonDocument.Parse(body).RootElement.GetProperty("title").GetString(),
            Is.EqualTo("The assistant is in the middle of changing your site."));
    }

    [Test]
    public async Task A_version_is_not_brought_back_underneath_a_turn()
    {
        var owner = await AccountAsync("restore-mid-turn@example.com");
        var site = await SiteAsync(owner, "Mid-turn Bakery");

        // A second version to go back from.
        Assert.That((await RenameAsync(owner, site, "Mid-turn Bakery & Café", applyToSite: true)).IsSuccessStatusCode);

        var versions = await VersionsAsync(owner, site);
        var first = versions[versions.GetArrayLength() - 1].GetProperty("nanoid").GetString();

        using (await TurnAsync(site))
        {
            await AssertRefusedForTheTurnAsync(await Client.SendAsync(
                Request(HttpMethod.Post, $"/api/sites/{site}/versions/{first}/restore", (AuthCookies.AccessTokenName, owner))));

            Assert.That((await VersionsAsync(owner, site)).GetArrayLength(), Is.EqualTo(versions.GetArrayLength()),
                "nothing written while it was refused");
        }

        var restored = await Client.SendAsync(
            Request(HttpMethod.Post, $"/api/sites/{site}/versions/{first}/restore", (AuthCookies.AccessTokenName, owner)));

        Assert.That(restored.StatusCode, Is.EqualTo(HttpStatusCode.OK), "and the same press works once the turn is over");
    }

    [Test]
    public async Task Renaming_the_pages_waits_for_the_turn_and_renaming_the_label_does_not()
    {
        var owner = await AccountAsync("rename-mid-turn@example.com");
        var site = await SiteAsync(owner, "Mid-turn Forge");

        using (await TurnAsync(site))
        {
            await AssertRefusedForTheTurnAsync(await RenameAsync(owner, site, "The Forge", applyToSite: true));

            Assert.That((await RenameAsync(owner, site, "The Forge", applyToSite: false)).IsSuccessStatusCode, Is.True,
                "the dashboard's label is a row, and nothing a turn does collides with it");
        }
    }

    [Test]
    public async Task A_photograph_is_not_added_or_removed_underneath_a_turn()
    {
        var owner = await AccountAsync("images-mid-turn@example.com");
        var site = await SiteAsync(owner, "Mid-turn Florist");

        Assert.That((await UploadAsync(owner, site, "before.gif")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (await TurnAsync(site))
        {
            await AssertRefusedForTheTurnAsync(await UploadAsync(owner, site, "during.gif"));
            await AssertRefusedForTheTurnAsync(await Client.SendAsync(
                Request(HttpMethod.Delete, $"/api/sites/{site}/images/before.gif", (AuthCookies.AccessTokenName, owner))));
        }

        Assert.That((await UploadAsync(owner, site, "after.gif")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
