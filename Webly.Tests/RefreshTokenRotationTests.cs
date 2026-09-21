using Webly.Services.UseCases.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Webly.Tests;

public class RefreshTokenRotationTests : PostgresTestBase
{
    private AuthenticationTestContext Auth() => new(CreateContext());

    private static Webly.Services.DTO.Authentication.RegisterRequest Registration() => new()
    {
        Email = "cili@example.com",
        Password = "matyas-kiraly-1458",
        DisplayName = "Cili"
    };

    [Test]
    public async Task Rotating_issues_a_new_token_and_revokes_the_one_presented()
    {
        using var auth = Auth();
        var session = await auth.RegisterUser.Execute(Registration());

        var rotated = await auth.RotateRefreshToken.Execute(session.RefreshToken!);

        Assert.That(rotated.Succeeded, Is.True);
        Assert.That(rotated.RefreshToken, Is.Not.EqualTo(session.RefreshToken));

        var oldHash = auth.TokenService.HashRefreshToken(session.RefreshToken!);
        var oldRow = await auth.Db.RefreshTokens.SingleAsync(x => x.TokenHash == oldHash);

        Assert.That(oldRow.RevokedAt, Is.Not.Null);
    }

    [Test]
    public async Task An_expired_token_is_rejected()
    {
        using var auth = Auth();
        var session = await auth.RegisterUser.Execute(Registration());

        var hash = auth.TokenService.HashRefreshToken(session.RefreshToken!);
        var row = await auth.Db.RefreshTokens.SingleAsync(x => x.TokenHash == hash);
        row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await auth.Db.SaveChangesAsync();

        var result = await auth.RotateRefreshToken.Execute(session.RefreshToken!);

        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task An_unknown_token_is_rejected()
    {
        using var auth = Auth();
        await auth.RegisterUser.Execute(Registration());

        var result = await auth.RotateRefreshToken.Execute("not-a-token-we-ever-issued");

        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task Replaying_a_spent_token_revokes_the_whole_chain()
    {
        using var auth = Auth();
        var first = await auth.RegisterUser.Execute(Registration());

        var second = await auth.RotateRefreshToken.Execute(first.RefreshToken!);
        Assert.That(second.Succeeded, Is.True);

        // Old enough to be a replay rather than a sibling. The grace window exists for the requests a browser
        // sends together; a token presented long after its successor was issued means two parties hold cookies
        // from this chain, and there is no way to tell which one is the legitimate user.
        await AgeTheRotationAsync(auth, first.RefreshToken!);

        var replay = await auth.RotateRefreshToken.Execute(first.RefreshToken!);
        Assert.That(replay.Succeeded, Is.False);

        var successorStillWorks = await auth.RotateRefreshToken.Execute(second.RefreshToken!);
        Assert.That(successorStillWorks.Succeeded, Is.False, "the successor must be revoked too, not just the replayed token");

        Assert.That(await auth.Db.RefreshTokens.AllAsync(x => x.RevokedAt != null), Is.True);
    }

    /// <summary>
    /// A browser does not send one request at a time. When the access token dies, every request already in
    /// flight arrives carrying the same live refresh cookie — so one of them rotates and the rest are replays
    /// of a token revoked a millisecond ago. Treating those as theft logged people out reliably, every fifteen
    /// minutes, on any page that loads more than one thing.
    /// </summary>
    [Test]
    public async Task A_replay_within_the_grace_window_is_the_rest_of_a_batch_rather_than_theft()
    {
        using var auth = Auth();
        var first = await auth.RegisterUser.Execute(Registration());

        var rotated = await auth.RotateRefreshToken.Execute(first.RefreshToken!);
        Assert.That(rotated.Succeeded, Is.True);

        var sibling = await auth.RotateRefreshToken.Execute(first.RefreshToken!);

        Assert.Multiple(async () =>
        {
            Assert.That(sibling.Succeeded, Is.True, "the sibling request is served rather than treated as a leak");
            Assert.That(sibling.AccessToken, Is.Not.Null, "with a usable access token");

            // The point of answering this way: no second refresh token is minted, so the successor the first
            // request issued stays the only live one and the cookie the browser ends up with is whichever
            // response landed last — which is that successor either way.
            Assert.That(sibling.RefreshToken, Is.Null, "and no second refresh token");

            Assert.That(
                await auth.Db.RefreshTokens.CountAsync(x => x.RevokedAt == null), Is.EqualTo(1),
                "the chain is untouched");
        });
    }

    /// <summary>
    /// The window is for rotation only. A token revoked by signing out is spent immediately, because a
    /// sign-out that keeps working for another thirty seconds is not a sign-out — which is what this was
    /// until `ReplacedAt` told the two kinds of revocation apart.
    /// </summary>
    [Test]
    public async Task The_grace_window_does_not_apply_to_signing_out()
    {
        using var auth = Auth();
        var registered = await auth.RegisterUser.Execute(Registration());

        // Through the tracker, not ExecuteUpdate: this context already has the row loaded from registering,
        // and a query would hand back that tracked instance rather than what the statement wrote.
        foreach (var token in await auth.Db.RefreshTokens.ToListAsync()) token.RevokedAt = DateTime.UtcNow;

        await auth.Db.SaveChangesAsync();

        var rotated = await auth.RotateRefreshToken.Execute(registered.RefreshToken!);

        Assert.That(rotated.Succeeded, Is.False);
    }

    /// <summary>
    /// Moves a rotation far enough into the past that the grace window has closed, by editing the row rather
    /// than by waiting thirty seconds in a test suite.
    /// </summary>
    private static async Task AgeTheRotationAsync(AuthenticationTestContext auth, string rawToken)
    {
        var hash = auth.TokenService.HashRefreshToken(rawToken);
        var stale = DateTime.UtcNow - RotateRefreshToken.ReuseGrace - TimeSpan.FromSeconds(1);

        // Through the tracker for the reason above.
        var token = await auth.Db.RefreshTokens.SingleAsync(x => x.TokenHash == hash);
        token.ReplacedAt = stale;

        await auth.Db.SaveChangesAsync();
    }
}
