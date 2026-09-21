using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Webly.Services.Services.Realtime;

namespace Webly.Tests;

/// <summary>
/// Sessions, which exist so that ending one can end the things it opened — today a realtime connection, whose
/// caller's identity is fixed at the handshake and was therefore outliving the sign-out that revoked it.
///
/// Two halves are tested here: that a session id is the thing that stays the same while its tokens change, and
/// that ending one ends the right connections and no others. The rules in the second half all have a defect
/// behind them — ending a user's connections instead of a session's left another device's socket dead and
/// silent, because <c>Context.Abort</c> closes cleanly enough that the SignalR client never reconnects.
/// </summary>
public class RealtimeSessionTests : PostgresTestBase
{
    private AuthenticationTestContext Auth() => new(CreateContext());

    private static Webly.Services.DTO.Authentication.RegisterRequest Registration(string email = "cili@example.com") =>
        new() { Email = email, Password = "matyas-kiraly-1458", DisplayName = "Cili" };

    /// <summary>The session claim out of an access token, without a JWT library: the payload is the middle segment.</summary>
    private static string? SessionIdOf(string accessToken)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var document = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(padded));

        return document.RootElement.TryGetProperty("sid", out var sid) ? sid.GetString() : null;
    }

    [Test]
    public async Task A_session_survives_the_rotation_of_its_tokens()
    {
        using var auth = Auth();
        var session = await auth.RegisterUser.Execute(Registration());
        var rotated = await auth.RotateRefreshToken.Execute(session.RefreshToken!);

        // The whole point of the id. A `jti` changes every fifteen minutes and a refresh hash every use, so a
        // session identified by either would be exactly as short-lived as the thing it exists to outlive.
        Assert.That(SessionIdOf(rotated.AccessToken!), Is.EqualTo(SessionIdOf(session.AccessToken!)));
        Assert.That(SessionIdOf(session.AccessToken!), Is.Not.Null.And.Not.Empty);

        var hash = auth.TokenService.HashRefreshToken(rotated.RefreshToken!);
        var row = await auth.Db.RefreshTokens.SingleAsync(x => x.TokenHash == hash);

        Assert.That(row.SessionId, Is.EqualTo(SessionIdOf(session.AccessToken!)));
    }

    [Test]
    public async Task Two_sign_ins_are_two_sessions()
    {
        using var auth = Auth();
        var first = await auth.RegisterUser.Execute(Registration());
        var second = await auth.SignInWithPassword.Execute(new()
        {
            Email = Registration().Email,
            Password = Registration().Password
        });

        // Signing out of one must not reach the other, which is only possible if they are told apart.
        Assert.That(SessionIdOf(second.AccessToken!), Is.Not.EqualTo(SessionIdOf(first.AccessToken!)));
    }

    [Test]
    public void Ending_a_session_ends_its_connections_and_leaves_the_others_alone()
    {
        var sessions = new RealtimeSessions(NullLogger<RealtimeSessions>.Instance);

        var ended = 0;
        var otherSession = 0;
        var otherUser = 0;

        sessions.Register(1, "session-a", () => ended++);
        sessions.Register(1, "session-b", () => otherSession++);
        sessions.Register(2, "session-a", () => otherUser++);

        sessions.End(1, "session-a");

        Assert.Multiple(() =>
        {
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(otherSession, Is.Zero, "another session of the same person is another device, and stays up");
            Assert.That(otherUser, Is.Zero, "a session id is only meaningful within one account");
        });
    }

    [Test]
    public void Ending_without_a_session_ends_everything_that_person_has_open()
    {
        var sessions = new RealtimeSessions(NullLogger<RealtimeSessions>.Instance);

        var named = 0;
        var unnamed = 0;

        sessions.Register(1, "session-a", () => named++);
        sessions.Register(1, null, () => unnamed++);

        // Signing out with no refresh cookie already revokes every token that person has, because there is
        // nothing to say which session it meant. The sockets follow the same rule.
        sessions.End(1, sessionId: null);

        Assert.That(named, Is.EqualTo(1));
        Assert.That(unnamed, Is.EqualTo(1));
    }

    [Test]
    public void A_connection_with_no_session_is_ended_whatever_is_named()
    {
        var sessions = new RealtimeSessions(NullLogger<RealtimeSessions>.Instance);

        var unnamed = 0;
        sessions.Register(1, null, () => unnamed++);

        // Its token predates the claim. It cannot be matched, and being unable to name something is not a
        // reason to leave it running as somebody who has signed out.
        sessions.End(1, "session-a");

        Assert.That(unnamed, Is.EqualTo(1));
    }

    [Test]
    public void A_connection_that_has_dropped_is_not_ended_twice()
    {
        var sessions = new RealtimeSessions(NullLogger<RealtimeSessions>.Instance);

        var aborted = 0;
        var registration = sessions.Register(1, "session-a", () => aborted++);

        registration.Dispose();
        sessions.End(1, "session-a");

        Assert.That(aborted, Is.Zero, "the disconnect forgets it, so a registry of dead sockets cannot accumulate");
    }
}
