using System.Net;
using System.Text.Json;
using Webly.Api.Infrastructure;

namespace Webly.Tests;

/// <summary>
/// The usage report over HTTP: what the platform costs is for the people named in Administrators:Emails, and a
/// signed-in customer asking for it is refused.
/// </summary>
public class UsageReportEndpointTests : AuthEndpointTestBase
{
    private const string Password = "hunyadi-matyas-1458";

    private async Task<string> AccountAsync(string email)
    {
        var registered = await Client.PostAsync("/api/auth/register",
            Json(new { email, password = Password, displayName = email.Split('@')[0] }));
        Assert.That(registered.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"registering {email}");

        var verified = await Client.PostAsync("/api/auth/email/verify", Json(new { token = LatestTokenFor(email) }));
        Assert.That(verified.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"verifying {email}");

        return CookieValue(verified, AuthCookies.AccessTokenName)!;
    }

    private Task<HttpResponseMessage> ReportAsync(string access, int days) =>
        Client.SendAsync(Request(HttpMethod.Get, $"/api/admin/usage?days={days}", (AuthCookies.AccessTokenName, access)));

    [Test]
    public async Task A_customer_is_refused_and_an_administrator_gets_the_whole_period()
    {
        var customer = await AccountAsync("customer@example.com");
        var administrator = await AccountAsync(AdministratorEmail);

        var refused = await ReportAsync(customer, 30);
        var report = await ReportAsync(administrator, 7);

        using var body = JsonDocument.Parse(await report.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(report.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(root.GetProperty("periodDays").GetInt32(), Is.EqualTo(7));
            Assert.That(root.GetProperty("byDay").GetArrayLength(), Is.EqualTo(7), "every day, the empty ones included");
            Assert.That(root.GetProperty("totals").GetProperty("turns").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public async Task Nobody_signed_in_gets_anything()
    {
        var response = await Client.GetAsync("/api/admin/usage");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
