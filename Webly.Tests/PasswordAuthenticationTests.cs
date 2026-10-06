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

    /// <summary>
    /// Ending every session ends what each session holds: its entry in the blacklist, which is what refuses its
    /// access tokens and preview cookies, and its open sockets. Revoking the refresh tokens alone was the old shape.
    /// </summary>
    [Test]
    public async Task Changing_the_password_ends_what_every_other_session_holds()
    {
        using var auth = Auth();
        await auth.RegisterUser.Execute(Registration());
        var user = await auth.Db.Users.SingleAsync();
        var sessions = await auth.Db.RefreshTokens.Select(x => x.SessionId).Distinct().ToListAsync();

        var aborted = false;
        using var socket = auth.RealtimeSessions.Register(user.Id, sessions.Single(), () => aborted = true);

        var changed = await auth.ChangePassword.Execute(user.Id, Registration().Password, "a-new-password-2026");

        Assert.Multiple(() =>
        {
            Assert.That(changed.Succeeded, Is.True);
            Assert.That(sessions.All(auth.AccessTokenBlacklist.IsSessionRevoked), Is.True, "every old session is refused");
            Assert.That(aborted, Is.True, "and its socket is closed");
        });
    }

    /// <summary>
    /// The theft rule: a rotated refresh token presented again after the grace window means somebody else holds the
    /// chain, so every session ends — the successor included.
    /// </summary>
    [Test]
    public async Task Replaying_a_rotated_refresh_token_ends_every_session()
    {
        using var auth = Auth();
        var first = await auth.RegisterUser.Execute(Registration());
        var successor = await auth.RotateRefreshToken.Execute(first.RefreshToken!);

        // Past the grace window that serves a browser's parallel requests.
        var spent = await auth.Db.RefreshTokens.SingleAsync(x => x.ReplacedAt != null);
        spent.ReplacedAt = DateTime.UtcNow.AddMinutes(-5);
        await auth.Db.SaveChangesAsync();

        var replay = await auth.RotateRefreshToken.Execute(first.RefreshToken!);
        var afterwards = await auth.RotateRefreshToken.Execute(successor.RefreshToken!);

        Assert.Multiple(() =>
        {
            Assert.That(replay.Succeeded, Is.False);
            Assert.That(afterwards.Succeeded, Is.False, "the successor went with it");
        });
    }

    /// <summary>
    /// A refresh token revoked on purpose has no successor, so presenting it again is somebody signed out rather than
    /// a thief, and nothing else ends. It used to count as theft.
    /// </summary>
    [Test]
    public async Task A_signed_out_refresh_token_presented_again_ends_nothing_else()
    {
        using var auth = Auth();
        var laptop = await auth.RegisterUser.Execute(Registration());
        var phone = await auth.SignInWithPassword.Execute(new LoginRequest { Email = "anna@example.com", Password = Registration().Password });
        var userId = (await auth.Db.Users.SingleAsync()).Id;

        await auth.SignOut.Execute(userId, laptop.RefreshToken);

        var stale = await auth.RotateRefreshToken.Execute(laptop.RefreshToken!);
        var other = await auth.RotateRefreshToken.Execute(phone.RefreshToken!);

        Assert.Multiple(() =>
        {
            Assert.That(stale.Succeeded, Is.False);
            Assert.That(other.Succeeded, Is.True, "the phone was never signed out");
        });
    }

    /// <summary>
    /// The case that found it: change the password on the laptop, and the phone's next request — holding a refresh
    /// cookie the change revoked — ended every session, the laptop's brand-new one included.
    /// </summary>
    [Test]
    public async Task The_device_that_changed_the_password_stays_signed_in_when_the_other_one_calls()
    {
        using var auth = Auth();
        await auth.RegisterUser.Execute(Registration());
        var phone = await auth.SignInWithPassword.Execute(new LoginRequest { Email = "anna@example.com", Password = Registration().Password });
        var userId = (await auth.Db.Users.SingleAsync()).Id;

        var laptop = await auth.ChangePassword.Execute(userId, Registration().Password, "a-new-password-2026");

        var phoneCalls = await auth.RotateRefreshToken.Execute(phone.RefreshToken!);
        var laptopCalls = await auth.RotateRefreshToken.Execute(laptop.RefreshToken!);

        Assert.Multiple(() =>
        {
            Assert.That(phoneCalls.Succeeded, Is.False, "the phone is signed out");
            Assert.That(laptopCalls.Succeeded, Is.True, "and the laptop is not");
        });
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
