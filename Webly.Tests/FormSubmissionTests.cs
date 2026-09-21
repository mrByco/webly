using System.Net;
using System.Text.Json;
using Webly.Api.Infrastructure;
using Webly.Services.UseCases.Forms;

namespace Webly.Tests;

/// <summary>
/// The form endpoint a published site posts to, driven the way a visitor's browser drives it: an ordinary form
/// post from another origin, with no cookie, no token and no JSON.
///
/// It is tested through HTTP rather than against <c>SubmitForm</c> because half of what matters here is the
/// HTTP: that it accepts a form body at all, that it needs no authentication while every other route under a
/// site does, that a stranger's post ends up in exactly one owner's list, and that the redirect it answers with
/// cannot be aimed anywhere but back where the visitor came from.
/// </summary>
public class FormSubmissionTests : AuthEndpointTestBase
{
    private const string Password = "hunyadi-matyas-1458";

    /// <summary>The address the published site is imagined to be at, which is what a real Referer would be.</summary>
    private const string SiteOrigin = "https://koopman-cycles.webly.site";

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

    /// <summary>A form post exactly as a browser on the published site would send it.</summary>
    private Task<HttpResponseMessage> PostAsync(
        string siteNanoid,
        IEnumerable<KeyValuePair<string, string>> fields,
        string? referer = SiteOrigin + "/contact/")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/public/forms/{siteNanoid}")
        {
            Content = new FormUrlEncodedContent(fields)
        };

        if (referer is not null) request.Headers.Referrer = new Uri(referer);

        return Client.SendAsync(request);
    }

    private async Task<JsonElement> SubmissionsAsync(string access, string siteNanoid)
    {
        var request = Request(HttpMethod.Get, $"/api/sites/{siteNanoid}/submissions",
            (AuthCookies.AccessTokenName, access));

        var response = await Client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Test]
    public async Task A_visitor_with_no_account_can_send_a_message_and_the_owner_gets_it()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        Emails.Sent.Clear();

        var response = await PostAsync(site,
        [
            new("_form", "contact"),
            new("_next", "?sent=1"),
            new("_ignore", string.Empty),
            new("Your name", "Jasper de Wit"),
            new("Your email", "jasper@example.com"),
            new("How can we help?", "Can you service a Gazelle?")
        ]);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther), await response.Content.ReadAsStringAsync());

        // Back to the page the form was on, with the form's own query appended — resolved against the Referer,
        // which is the only thing this endpoint knows about where the visitor was.
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"{SiteOrigin}/contact/?sent=1"));

        var submissions = await SubmissionsAsync(owner, site);

        Assert.That(submissions.GetArrayLength(), Is.EqualTo(1));

        var submission = submissions[0];
        var fields = submission.GetProperty("fields");

        Assert.Multiple(() =>
        {
            Assert.That(submission.GetProperty("formName").GetString(), Is.EqualTo("contact"));

            // The three the visitor filled in, and none of the three the form used to talk to us. A list that
            // showed `_next` as if somebody had typed it would be the owner reading our plumbing.
            Assert.That(fields.GetArrayLength(), Is.EqualTo(3), fields.ToString());
            Assert.That(fields.EnumerateArray().Select(x => x.GetProperty("name").GetString()),
                Is.EquivalentTo(new[] { "Your name", "Your email", "How can we help?" }));
            Assert.That(fields[2].GetProperty("value").GetString(), Is.EqualTo("Can you service a Gazelle?"));
        });

        Assert.That(Emails.Sent, Has.Count.EqualTo(1), "the owner is told, because nobody watches an editor tab");

        var mail = Emails.Last;

        Assert.Multiple(() =>
        {
            Assert.That(mail.To, Is.EqualTo("owner@example.com"));

            // The point of the whole notification: reply goes to the customer, not to us.
            Assert.That(mail.ReplyTo, Is.EqualTo("jasper@example.com"));
            Assert.That(mail.Subject, Does.Contain("Koopman Cycles"));
            Assert.That(mail.TextBody, Does.Contain("Can you service a Gazelle?"));
        });
    }

    [Test]
    public async Task A_filled_in_honeypot_is_answered_as_a_success_and_stored_nowhere()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        Emails.Sent.Clear();

        var response = await PostAsync(site,
        [
            new("Your name", "definitely a person"),
            new("_ignore", "http://buy-cheap-links.example")
        ]);

        // A redirect, exactly as a real submission gets. Telling a bot it was caught is telling it what to
        // change, and the field it filled in is the only thing that gave it away.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        var submissions = await SubmissionsAsync(owner, site);

        Assert.Multiple(() =>
        {
            Assert.That(submissions.GetArrayLength(), Is.Zero, "nothing is stored");
            Assert.That(Emails.Sent, Is.Empty, "and nobody is emailed");
        });
    }

    [Test]
    public async Task A_form_that_sent_nothing_says_so_rather_than_storing_a_blank()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        // Only the form's own hidden fields: a mis-wired form, or a bot probing the endpoint. Either way the
        // owner's list must not fill up with rows that say nothing.
        var response = await PostAsync(site, [new("_form", "contact"), new("_next", "?sent=1")]);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await SubmissionsAsync(owner, site)).GetArrayLength(), Is.Zero);
    }

    [Test]
    public async Task A_message_too_long_to_be_one_is_refused()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        var response = await PostAsync(site,
        [
            new("message", new string('x', SubmitForm.MaxValueLength + 1))
        ]);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await SubmissionsAsync(owner, site)).GetArrayLength(), Is.Zero);
    }

    [Test]
    public async Task The_redirect_cannot_be_aimed_away_from_the_page_the_form_was_on()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        // Each of these is a way to turn a form post into a redirect somewhere else. None of them may produce a
        // Location outside the Referer: the endpoint answers with the page the visitor was on instead.
        foreach (var next in new[]
                 {
                     "https://phish.example/login",
                     "//phish.example/login",
                     "/somewhere-else",
                     "../../etc",
                     "\\\\phish.example"
                 })
        {
            var response = await PostAsync(site, [new("_next", next), new("message", "hello")]);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther), next);
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"{SiteOrigin}/contact/"), next);
        }
    }

    [Test]
    public async Task With_no_referer_the_visitor_gets_a_page_saying_the_message_was_sent()
    {
        var owner = await AccountAsync("owner@example.com");
        var site = await SiteAsync(owner, "Koopman Cycles");

        // Somebody testing the endpoint by hand, or a browser configured not to send one. The submission still
        // counts; what changes is that there is nowhere to send them back to, so they get told here.
        var response = await PostAsync(site, [new("message", "hello")], referer: null);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        });

        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("your message has been sent"));
        Assert.That((await SubmissionsAsync(owner, site)).GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public async Task A_form_pointed_at_a_site_that_does_not_exist_answers_404()
    {
        var response = await PostAsync("not-a-site-at-all", [new("message", "hello")]);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_visitor_to_one_site_cannot_reach_another_owner_is_list()
    {
        var owner = await AccountAsync("owner@example.com");
        var stranger = await AccountAsync("stranger@example.com");

        var site = await SiteAsync(owner, "Koopman Cycles");

        await PostAsync(site, [new("message", "hello")]);

        var request = Request(HttpMethod.Get, $"/api/sites/{site}/submissions",
            (AuthCookies.AccessTokenName, stranger));

        var theirs = await Client.SendAsync(request);

        // 404 rather than 403, the rule every per-site route follows: a 403 would confirm the nanoid names a
        // real site. SiteIsolationTests drives this route too, for the same reason.
        Assert.That(theirs.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
