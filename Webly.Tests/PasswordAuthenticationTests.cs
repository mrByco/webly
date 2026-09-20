using Webly.Data.Models.Authentication;
using Webly.Services.DTO.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Webly.Tests;

public class PasswordAuthenticationTests : PostgresTestBase
{
    private AuthenticationTestContext Auth() => new(CreateContext());

    private static RegisterRequest Registration(string email = "anna@example.com") => new()
    {
        Email = email,
        Password = "hunyadi-janos-1456",
        DisplayName = "Anna"
    };

    [Test]
    public async Task Registering_creates_a_user_with_a_hashed_password_and_no_site()
    {
        using var auth = Auth();

        var result = await auth.RegisterUser.Execute(Registration());

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Me!.HasSite, Is.False, "creating the first site is its own step, not part of registration");
        Assert.That(result.Me.HasPassword, Is.True);

        var user = await auth.Db.Users.SingleAsync();

        Assert.That(user.PasswordHash, Is.Not.Null.And.Not.EqualTo("hunyadi-janos-1456"));
        Assert.That(user.CurrentSiteId, Is.Null);
    }

    [Test]
    public async Task Registering_normalizes_the_email_and_rejects_a_duplicate_in_any_casing()
    {
        using var auth = Auth();

        await auth.RegisterUser.Execute(Registration("  Anna@Example.COM "));

        var user = await auth.Db.Users.SingleAsync();
        Assert.That(user.Email, Is.EqualTo("anna@example.com"));

        var duplicate = await auth.RegisterUser.Execute(Registration("ANNA@example.com"));

        Assert.That(duplicate.Succeeded, Is.False);
        Assert.That(duplicate.Error, Is.EqualTo(AuthError.EmailAlreadyRegistered));
    }

    [Test]
    public async Task Signing_in_succeeds_with_the_right_password_and_issues_both_tokens()
    {
        using var auth = Auth();
        await auth.RegisterUser.Execute(Registration());

        var result = await auth.SignInWithPassword.Execute(
            new LoginRequest { Email = "ANNA@example.com", Password = "hunyadi-janos-1456" });

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.AccessToken, Is.Not.Empty);
        Assert.That(result.RefreshToken, Is.Not.Empty);
    }

    [Test]
    public async Task A_wrong_password_and_an_unknown_email_fail_identically()
    {
        using var auth = Auth();
        await auth.RegisterUser.Execute(Registration());

        var wrongPassword = await auth.SignInWithPassword.Execute(
            new LoginRequest { Email = "anna@example.com", Password = "not-the-password" });

        var unknownEmail = await auth.SignInWithPassword.Execute(
            new LoginRequest { Email = "nobody@example.com", Password = "hunyadi-janos-1456" });

        // Same answer for both, so the endpoint cannot be used to discover who has an account.
        Assert.That(wrongPassword.Error, Is.EqualTo(AuthError.InvalidCredentials));
        Assert.That(unknownEmail.Error, Is.EqualTo(AuthError.InvalidCredentials));
    }

    [Test]
    public async Task An_account_with_no_password_cannot_be_signed_into_with_one()
    {
        using var auth = Auth();

        await auth.SignInWithExternalLogin.Execute(GoogleInfo());

        var result = await auth.SignInWithPassword.Execute(
            new LoginRequest { Email = "bela@example.com", Password = "" });

        Assert.That(result.Error, Is.EqualTo(AuthError.InvalidCredentials));
    }

    [Test]
    public async Task Signing_out_revokes_the_session_so_the_refresh_token_stops_working()
    {
        using var auth = Auth();
        var registered = await auth.RegisterUser.Execute(Registration());
        var userId = (await auth.Db.Users.SingleAsync()).Id;

        await auth.SignOut.Execute(userId, registered.RefreshToken);

        var rotated = await auth.RotateRefreshToken.Execute(registered.RefreshToken!);

        Assert.That(rotated.Succeeded, Is.False);
    }

    private static ExternalLoginInfo GoogleInfo() => new()
    {
        Provider = ExternalLoginProvider.Google,
        ProviderKey = "google-sub-1",
        Email = "bela@example.com",
        EmailVerified = true,
        DisplayName = "Jamie"
    };
}
