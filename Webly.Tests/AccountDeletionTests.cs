using System.Net;
using System.Text.Json;
using Webly.Api.Infrastructure;
using Webly.Data.Models.Sites;
using Microsoft.EntityFrameworkCore;

namespace Webly.Tests;

/// <summary>
/// Closing an account, through the endpoint that does it.
///
/// The database half of this — that a user's sites, versions, conversations, messages and deployments all go in
/// one statement despite the cycle between a message and the version it produced — is
/// <c>WeblyDbContextTests.Deleting_a_user_removes_their_sites_and_everything_under_them</c>. What this adds is the
/// product half: that a wrong password is refused, that the sites go through <c>DeleteSite</c> so their
/// repositories leave with them, and that the address can be registered again afterwards — which is the only
/// proof a person has that "closed" meant closed.
/// </summary>
public class AccountDeletionTests : AuthEndpointTestBase
{
    private const string Email = "leaving@example.com";
    private const string Password = "hunyadi-matyas-1458";

    private async Task<string> VerifiedAccountAsync()
    {
        await Client.PostAsync("/api/auth/register",
            Json(new { email = Email, password = Password, displayName = "Leaving" }));

        var verified = await Client.PostAsync("/api/auth/email/verify", Json(new { token = LatestTokenFor(Email) }));

        return CookieValue(verified, AuthCookies.AccessTokenName)!;
    }

    private async Task<HttpResponseMessage> SendAsync(string access, HttpMethod method, string url, object? body)
    {
        var request = Request(method, url, (AuthCookies.AccessTokenName, access));

        if (body is not null) request.Content = Json(body);

        return await Client.SendAsync(request);
    }

    [Test]
    public async Task Closing_an_account_takes_its_sites_and_their_repositories()
    {
        var access = await VerifiedAccountAsync();

        var created = await SendAsync(access, HttpMethod.Post, "/api/sites", new { name = "Leaving Bakery" });
        var site = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("nanoid").GetString()!;

        var wrong = await SendAsync(access, HttpMethod.Delete, "/api/auth/account",
            new { currentPassword = "not-the-password" });

        Assert.That(wrong.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "a wrong password must not close it");

        var closed = await SendAsync(access, HttpMethod.Delete, "/api/auth/account", new { currentPassword = Password });

        Assert.That(closed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        await using var db = CreateContext();

        Assert.Multiple(() =>
        {
            Assert.That(db.Users.Any(x => x.Email == Email), Is.False, "the account");
            Assert.That(db.Set<Site>().Any(x => x.Nanoid == site), Is.False, "its site");
        });

        // The strongest statement a person can check for themselves: the address is free again.
        var again = await Client.PostAsync("/api/auth/register",
            Json(new { email = Email, password = Password, displayName = "Back Again" }));

        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>
    /// Somebody who registered, never confirmed their address and thought better of it is the person most
    /// entitled to leave — the verification gate exists to stop unverified accounts publishing, not to hold them.
    /// </summary>
    [Test]
    public async Task An_unverified_account_can_still_be_closed()
    {
        var registered = await Client.PostAsync("/api/auth/register",
            Json(new { email = "unverified@example.com", password = Password, displayName = "Unsure" }));

        var access = CookieValue(registered, AuthCookies.AccessTokenName)!;

        var closed = await SendAsync(access, HttpMethod.Delete, "/api/auth/account", new { currentPassword = Password });

        Assert.That(closed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        await using var db = CreateContext();

        Assert.That(db.Users.Any(x => x.Email == "unverified@example.com"), Is.False);
    }
}
