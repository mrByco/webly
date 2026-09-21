using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Webly.Api.Infrastructure;
using Webly.Data;
using Webly.Data.Models.Deployments;
using Microsoft.EntityFrameworkCore;

namespace Webly.Tests;

/// <summary>
/// What the editor is told about a publish that is already running.
///
/// A deployment's run id <b>is</b> its nanoid, deliberately, which is what makes a publish the one run in this
/// product that can outlive the process that started it — and nothing used that, because nothing on load knew a
/// publish was in flight. Reload the editor mid-publish and the button read "Publish" again, over a publish that
/// was already going; pressing it was harmless, since the partial unique index refuses a second and
/// <c>PublishSite</c> hands back the one that is running, but the screen was lying until somebody pressed.
/// Found by reloading the page during a real publish.
/// </summary>
public class SitePublishStateTests : AuthEndpointTestBase
{
    private const string Password = "hunyadi-matyas-1458";

    private async Task<string> AccountAsync(string email)
    {
        await Client.PostAsync("/api/auth/register",
            Json(new { email, password = Password, displayName = email.Split('@')[0] }));

        var verified = await Client.PostAsync("/api/auth/email/verify", Json(new { token = LatestTokenFor(email) }));

        return CookieValue(verified, AuthCookies.AccessTokenName)!;
    }

    private async Task<JsonElement> SiteAsync(string access, string nanoid)
    {
        var response = await Client.SendAsync(
            Request(HttpMethod.Get, $"/api/sites/{nanoid}", (AuthCookies.AccessTokenName, access)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Test]
    public async Task A_site_reports_the_publish_that_is_running_and_nothing_when_none_is()
    {
        var access = await AccountAsync("owner@example.com");

        var created = await Client.SendAsync(Request(HttpMethod.Post, "/api/sites", (AuthCookies.AccessTokenName, access))
            .With(request => request.Content = Json(new { name = "Koopman Cycles" })));

        var nanoid = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("nanoid").GetString()!;

        Assert.That(
            (await SiteAsync(access, nanoid)).GetProperty("activeDeploymentNanoid").ValueKind,
            Is.EqualTo(JsonValueKind.Null),
            "a site nobody has published is not publishing");

        // A row in a live status, written directly: going through the publish endpoint would run a real build,
        // and what this is about is what the *editor* is told rather than what the builder does. `Building`
        // rather than `Queued`, deliberately — the job runner leases queued rows, and a test that hands it one
        // would start a sandbox.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeblyDbContext>();

        var site = await db.Sites.FirstAsync(x => x.Nanoid == nanoid);

        var deployment = new Deployment
        {
            SiteId = site.Id,
            SiteVersionId = site.HeadVersionId!.Value,
            Status = DeploymentStatus.Building,
            TriggeredByUserId = site.OwnerId
        };

        db.Deployments.Add(deployment);
        await db.SaveChangesAsync();

        Assert.That(
            (await SiteAsync(access, nanoid)).GetProperty("activeDeploymentNanoid").GetString(),
            Is.EqualTo(deployment.Nanoid),
            "and the editor is handed the run id it needs to re-attach");

        // A finished one is not something to re-attach to: the button would sit on "Publishing" for ever.
        deployment.Status = DeploymentStatus.Ready;
        await db.SaveChangesAsync();

        Assert.That(
            (await SiteAsync(access, nanoid)).GetProperty("activeDeploymentNanoid").ValueKind,
            Is.EqualTo(JsonValueKind.Null));
    }
}

internal static class RequestExtensions
{
    public static HttpRequestMessage With(this HttpRequestMessage request, Action<HttpRequestMessage> configure)
    {
        configure(request);
        return request;
    }
}
