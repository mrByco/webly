using Webly.Data.Models.Authentication;
using Webly.Services.DTO.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Webly.Tests;

public class EmailVerificationTests : PostgresTestBase
{
    private AuthenticationTestContext Auth() => new(CreateContext());

    private static RegisterRequest Registration() => new()
    {
        Email = "eszter@example.com",
        Password = "rakoczi-ferenc-1703",
        DisplayName = "Eszter"
    };

    [Test]
    public async Task Registering_sends_a_verification_mail_and_leaves_the_address_unverified()
    {
        using var auth = Auth();

        var result = await auth.RegisterUser.Execute(Registration());

        Assert.That(result.Me!.EmailVerified, Is.False);
        Assert.That(auth.Emails.Sent, Has.Count.EqualTo(1));
        Assert.That(auth.Emails.Last.To, Is.EqualTo("eszter@example.com"));

        // Both bodies, always — clients that prefer text should not get an empty message, and a
        // text part is part of not looking like spam.
        Assert.That(auth.Emails.Last.HtmlBody, Is.Not.Empty);
        Assert.That(auth.Emails.Last.TextBody, Is.Not.Empty);
    }

    [Test]
    public async Task The_mailed_link_verifies_the_address_and_cannot_be_used_twice()
    {
        using var auth = Auth();
        await auth.RegisterUser.Execute(Registration());

        var token = auth.Emails.TokenFromLastLink();
        var verified = await auth.VerifyEmail.Execute(token);

        Assert.That(verified.Succeeded, Is.True);
        Assert.That(verified.Me!.EmailVerified, Is.True);
        Assert.That((await auth.Db.Users.SingleAsync()).EmailVerifiedAt, Is.Not.Null);

        var replay = await auth.VerifyEmail.Execute(token);

        Assert.That(replay.Succeeded, Is.False);
        Assert.That(replay.Error, Is.EqualTo(AuthError.InvalidToken));
    }

    [Test]
    public async Task An_expired_or_unknown_link_is_refused()
    {
        using var auth = Auth();
        await auth.RegisterUser.Execute(Registration());

        var token = auth.Emails.TokenFromLastLink();
        var row = await auth.Db.UserSecurityTokens.SingleAsync();
        row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await auth.Db.SaveChangesAsync();

        Assert.That((await auth.VerifyEmail.Execute(token)).Succeeded, Is.False);
        Assert.That((await auth.VerifyEmail.Execute("never-issued")).Succeeded, Is.False);
    }

    [Test]
    public async Task Issuing_a_new_link_retires_the_previous_one()
    {
        using var auth = Auth();
        await auth.RegisterUser.Execute(Registration());
        var firstToken = auth.Emails.TokenFromLastLink();

        var user = await auth.Db.Users.SingleAsync();
        await auth.SendEmailVerification.Execute(user);
        var secondToken = auth.Emails.TokenFromLastLink();

        Assert.That(secondToken, Is.Not.EqualTo(firstToken));

        // An older email forwarded or found later must not still open the account.
        Assert.That((await auth.VerifyEmail.Execute(firstToken)).Succeeded, Is.False);
        Assert.That((await auth.VerifyEmail.Execute(secondToken)).Succeeded, Is.True);
    }

    [Test]
    public async Task A_verification_token_cannot_be_used_as_a_password_reset()
    {
        using var auth = Auth();
        await auth.RegisterUser.Execute(Registration());

        var verificationToken = auth.Emails.TokenFromLastLink();

        // The purpose is mixed into the hash, so the same string simply does not exist in the other
        // namespace. This is what stops a low-value link being redeemed as a high-value one.
        var reset = await auth.ResetPassword.Execute(verificationToken, "uj-jelszo-12345");

        Assert.That(reset.Succeeded, Is.False);
        Assert.That(reset.Error, Is.EqualTo(AuthError.InvalidToken));
    }

    [Test]
    public async Task A_reset_token_cannot_be_used_to_verify_an_address()
    {
        using var auth = Auth();
        await auth.RegisterUser.Execute(Registration());

        await auth.RequestPasswordReset.Execute("eszter@example.com");
        var resetToken = auth.Emails.TokenFromLastLink();

        Assert.That((await auth.VerifyEmail.Execute(resetToken)).Succeeded, Is.False);
    }

    [Test]
    public async Task Verification_is_skipped_for_an_already_verified_address()
    {
        using var auth = Auth();
        await auth.RegisterUser.Execute(Registration());
        await auth.VerifyEmail.Execute(auth.Emails.TokenFromLastLink());

        var user = await auth.Db.Users.SingleAsync();
        var sent = await auth.SendEmailVerification.Execute(user);

        Assert.That(sent, Is.False);
        Assert.That(auth.Emails.Sent, Has.Count.EqualTo(1), "no second mail for an address already proven");
    }

    [Test]
    public async Task Verifying_stales_the_access_tokens_that_still_claim_otherwise()
    {
        using var auth = Auth();
        var session = await auth.RegisterUser.Execute(Registration());
        var userId = (await auth.Db.Users.SingleAsync()).Id;

        await auth.VerifyEmail.Execute(auth.Emails.TokenFromLastLink());

        // The token minted at registration says "unverified" and would keep saying so for its whole
        // lifetime. Marking those stale is what makes the user's other devices refresh into the truth.
        Assert.That(
            auth.AccessTokenBlacklist.IsRevoked("some-jti", userId, tokenSaysEmailVerified: false),
            Is.True);

        // The replacement, which does say verified, must not be caught by the same sweep — otherwise
        // the session could never recover.
        Assert.That(
            auth.AccessTokenBlacklist.IsRevoked("some-jti", userId, tokenSaysEmailVerified: true),
            Is.False);

        _ = session;
    }
}
