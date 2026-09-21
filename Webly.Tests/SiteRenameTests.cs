using System.Net;
using System.Text.Json;
using Webly.Api.Infrastructure;

namespace Webly.Tests;

/// <summary>
/// Renaming a site, which is two names rather than one: the label this dashboard uses, and the name the site's
/// own pages say — its header, its footer and the title of every link somebody shares.
///
/// They were one operation short of each other. A rename changed the row, so somebody who renamed "My Shop" to
/// "Ridgeway Cycles" had a dashboard saying one thing and a website saying the other, with nothing on screen
/// admitting it. The site's copy now moves too, when asked, and as a version like everything else that changes
/// a site — which is what these assert: the two files, the summary a person can read, and the cases where
/// nothing should be written at all.
/// </summary>
public class SiteRenameTests : AuthEndpointTestBase
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

    private async Task<HttpResponseMessage> RenameAsync(string access, string site, string name, bool applyToSite)
    {
        var request = Request(HttpMethod.Put, $"/api/sites/{site}", (AuthCookies.AccessTokenName, access));
        request.Content = Json(new { name, applyToSite });

        return await Client.SendAsync(request);
    }

    /// <summary>
    /// The site's own copy of <c>src/site.ts</c>, as text.
    ///
    /// Through the file endpoint the Code tab uses, so this reads what the customer would see rather than
    /// reaching into a repository — and the <c>text</c> property rather than the response body, which is JSON
    /// and would hand back a name's escaping escaped a second time.
    /// </summary>
    private async Task<string> SourceAsync(string access, string site)
    {
        var file = await ReadAsync(await Client.SendAsync(
            Request(HttpMethod.Get, $"/api/sites/{site}/file?path=src/site.ts",
                (AuthCookies.AccessTokenName, access))));

        return file.GetProperty("text").GetString()!;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<JsonElement> VersionsAsync(string access, string site) =>
        await ReadAsync(await Client.SendAsync(
            Request(HttpMethod.Get, $"/api/sites/{site}/versions", (AuthCookies.AccessTokenName, access))));

    [Test]
    public async Task Renaming_can_change_the_name_the_pages_say()
    {
        var owner = await AccountAsync("rename-applies@example.com");
        var site = await SiteAsync(owner, "My Shop");

        Assert.That(await SourceAsync(owner, site), Does.Contain("siteName = 'My Shop'"));

        var renamed = await RenameAsync(owner, site, "Ridgeway Cycles", applyToSite: true);

        Assert.That(renamed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(await SourceAsync(owner, site), Does.Contain("siteName = 'Ridgeway Cycles'"));

        var versions = await VersionsAsync(owner, site);

        Assert.Multiple(() =>
        {
            Assert.That(versions.GetArrayLength(), Is.EqualTo(2), "the rename is its own version, over the first commit");

            // What the history says, which is read by the person who did it rather than by us.
            Assert.That(versions[0].GetProperty("summary").GetString(), Is.EqualTo("Renamed the site to \"Ridgeway Cycles\""));
            Assert.That(versions[0].GetProperty("origin").GetString(), Is.EqualTo("Manual"));
            Assert.That(versions[0].GetProperty("changedFileCount").GetInt32(), Is.EqualTo(2),
                "the source constant and the agent's note of the business's name, and nothing else");
        });
    }

    [Test]
    public async Task Renaming_without_asking_leaves_the_pages_alone()
    {
        var owner = await AccountAsync("rename-label-only@example.com");
        var site = await SiteAsync(owner, "My Shop");

        var renamed = await RenameAsync(owner, site, "A Label Only", applyToSite: false);

        Assert.That(renamed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // The dashboard's name moved and the site's did not, which is the whole point of the choice.
        var detail = await ReadAsync(await Client.SendAsync(
            Request(HttpMethod.Get, $"/api/sites/{site}", (AuthCookies.AccessTokenName, owner))));

        Assert.Multiple(async () =>
        {
            Assert.That(detail.GetProperty("summary").GetProperty("name").GetString(), Is.EqualTo("A Label Only"));
            Assert.That(await SourceAsync(owner, site), Does.Contain("siteName = 'My Shop'"));
            Assert.That((await VersionsAsync(owner, site)).GetArrayLength(), Is.EqualTo(1), "nothing was committed");
        });
    }

    [Test]
    public async Task Renaming_to_the_same_name_commits_nothing()
    {
        var owner = await AccountAsync("rename-noop@example.com");
        var site = await SiteAsync(owner, "My Shop");

        await RenameAsync(owner, site, "My Shop", applyToSite: true);

        // The tree comes out identical, and a version that changed nothing is not a version. Worth its own
        // test because the shape that produces it — rewrite, compare, commit — is easy to replace with one
        // that writes unconditionally.
        Assert.That((await VersionsAsync(owner, site)).GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public async Task An_apostrophe_in_the_name_reaches_the_source_escaped()
    {
        var owner = await AccountAsync("rename-apostrophe@example.com");
        var site = await SiteAsync(owner, "My Shop");

        await RenameAsync(owner, site, "Joe's Kitchens", applyToSite: true);

        // This is customer input reaching a file that gets compiled, by a build whose failure is ours to
        // explain. Unescaped, the literal ends early and the site does not build at all.
        Assert.That(await SourceAsync(owner, site), Does.Contain(@"siteName = 'Joe\'s Kitchens'"));
    }
}
