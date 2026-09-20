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

        // The old token turning up again means two parties hold cookies from this chain, and there
        // is no way to tell which one is the legitimate user.
        var replay = await auth.RotateRefreshToken.Execute(first.RefreshToken!);
        Assert.That(replay.Succeeded, Is.False);

        var successorStillWorks = await auth.RotateRefreshToken.Execute(second.RefreshToken!);
        Assert.That(successorStillWorks.Succeeded, Is.False, "the successor must be revoked too, not just the replayed token");

        Assert.That(await auth.Db.RefreshTokens.AllAsync(x => x.RevokedAt != null), Is.True);
    }
}
