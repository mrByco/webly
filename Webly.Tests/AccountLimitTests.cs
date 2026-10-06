using Webly.Services.DTO.Authentication;
using Webly.Services.UseCases.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace Webly.Tests;

/// <summary>
/// The two account endpoints a stranger can call as often as they like: signing in, which is one request per
/// password guessed, and signing up, which mails whatever address was typed. Both were unlimited. This fixture runs
/// with the real limits, which every other one lifts — see <see cref="AuthEndpointTestBase.LiftsAccountLimits"/>.
/// </summary>
public class AccountLimitTests : AuthEndpointTestBase
{
    protected override bool LiftsAccountLimits => false;

    private const int Limit = 10;

    [Test]
    public async Task Guessing_passwords_from_one_address_is_stopped()
    {
        // Made without the endpoint, so this test spends nothing from the sign-up budget the other one counts.
        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RegisterUser>().Execute(new RegisterRequest
            {
                Email = "guessed@example.com",
                Password = "the-real-password-1",
                DisplayName = "Guessed"
            });
        }

        for (var i = 0; i < Limit; i++)
        {
            var guess = await Client.PostAsync(
                "/api/auth/login", Json(new { email = "guessed@example.com", password = $"guess-number-{i}" }));

            Assert.That(guess.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), $"guess {i + 1}");
        }

        // The right password too, once the limit is reached: what the limit stops is the evaluating of guesses, and a
        // guesser cannot be told apart from the owner by whether this one happens to be right.
        var refused = await Client.PostAsync(
            "/api/auth/login", Json(new { email = "guessed@example.com", password = "the-real-password-1" }));

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(refused.Headers.RetryAfter, Is.Not.Null);
        Assert.That(
            (await refused.Content.ReadFromJsonAsync<ProblemDetails>())!.Title,
            Is.EqualTo("There have been a lot of sign-in attempts from here."));
    }

    [Test]
    public async Task Signing_up_from_one_address_is_limited()
    {
        for (var i = 0; i < Limit; i++)
        {
            var signedUp = await Client.PostAsync(
                "/api/auth/register",
                Json(new { email = $"stranger-{i}@example.com", password = "a-password-123", displayName = $"Stranger {i}" }));

            Assert.That(signedUp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"sign-up {i + 1}");
        }

        var refused = await Client.PostAsync(
            "/api/auth/register",
            Json(new { email = "one-too-many@example.com", password = "a-password-123", displayName = "One too many" }));

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(
            (await refused.Content.ReadFromJsonAsync<ProblemDetails>())!.Title,
            Is.EqualTo("A lot of accounts have been created from here recently."));
        Assert.That(Emails.Sent.Select(x => x.To), Does.Not.Contain("one-too-many@example.com"));
        Assert.That(Emails.Sent, Has.Count.EqualTo(Limit));
    }
}
