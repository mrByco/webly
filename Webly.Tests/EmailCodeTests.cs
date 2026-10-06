using Webly.Services.DTO.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Webly.Tests;

/// <summary>
/// The six digits in a confirmation email. A space a machine walks in seconds, so what makes it safe is a count
/// of tries — and a count is a limit only if it holds when the tries arrive together.
/// </summary>
public class EmailCodeTests : PostgresTestBase
{
    private static RegisterRequest Registration() => new()
    {
        Email = "ilona@example.com",
        Password = "kossuth-lajos-1848",
        DisplayName = "Ilona"
    };

    private static int MaxAttempts => AuthenticationTestContext.EmailTokens.MaxCodeAttempts;

    /// <summary>A code that is certainly not <paramref name="code"/>, and a different one for each <paramref name="nth"/>.</summary>
    private static string Wrong(string code, int nth) => ((int.Parse(code) + 1 + nth) % 1_000_000).ToString("D6");

    private async Task<(AuthenticationTestContext Auth, int UserId, string Code)> RegisteredAsync()
    {
        var auth = new AuthenticationTestContext(CreateContext());
        await auth.RegisterUser.Execute(Registration());

        return (auth, (await auth.Db.Users.SingleAsync()).Id, auth.Emails.CodeFromLast());
    }

    [Test]
    public async Task The_mailed_code_confirms_the_address()
    {
        var (auth, userId, code) = await RegisteredAsync();
        using var _ = auth;

        var result = await auth.VerifyEmailWithCode.Execute(userId, code);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Me!.EmailVerified, Is.True);
    }

    [Test]
    public async Task The_right_code_still_works_on_its_last_try()
    {
        var (auth, userId, code) = await RegisteredAsync();
        using var _ = auth;

        for (var i = 0; i < MaxAttempts - 1; i++)
            Assert.That((await auth.VerifyEmailWithCode.Execute(userId, Wrong(code, i))).Succeeded, Is.False);

        Assert.That((await auth.VerifyEmailWithCode.Execute(userId, code)).Succeeded, Is.True);
    }

    [Test]
    public async Task Enough_wrong_codes_retire_it_and_the_right_one_after_them_is_refused()
    {
        var (auth, userId, code) = await RegisteredAsync();
        using var _ = auth;

        for (var i = 0; i < MaxAttempts; i++)
            await auth.VerifyEmailWithCode.Execute(userId, Wrong(code, i));

        var result = await auth.VerifyEmailWithCode.Execute(userId, code);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Error, Is.EqualTo(AuthError.InvalidToken));
    }

    [Test]
    public async Task Wrong_codes_sent_at_once_each_spend_a_try()
    {
        var (auth, userId, code) = await RegisteredAsync();
        using var _ = auth;

        // The burst that used to get through: the count was read, the code compared and the count written back,
        // so every request in it read the same count. Sixty wrong codes at the running app left it at four and the
        // right code was accepted after them. One context per request, opened before the start, so they really do
        // arrive together rather than queueing behind a connection each.
        const int burst = 40;
        var callers = Enumerable.Range(0, burst).Select(_ => new AuthenticationTestContext(CreateContext())).ToList();

        try
        {
            foreach (var caller in callers)
                await caller.Db.Database.OpenConnectionAsync();

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var guesses = callers.Select((caller, i) => Task.Run(async () =>
            {
                await start.Task;
                return await caller.VerifyEmailWithCode.Execute(userId, Wrong(code, i));
            })).ToList();

            start.SetResult();

            Assert.That((await Task.WhenAll(guesses)).Any(x => x.Succeeded), Is.False);
        }
        finally
        {
            foreach (var caller in callers)
                caller.Dispose();
        }

        // A context of its own, so nothing tracked from before the burst can answer instead of the database.
        using var after = new AuthenticationTestContext(CreateContext());

        Assert.That(
            (await after.VerifyEmailWithCode.Execute(userId, code)).Succeeded,
            Is.False,
            "a burst of wrong codes has to spend the tries, so the right one after it finds none left");
        Assert.That(
            (await after.Db.UserSecurityTokens.SingleAsync()).CodeAttempts,
            Is.EqualTo(MaxAttempts),
            "exactly the tries there were, and nothing past them");
    }

    [Test]
    public async Task The_link_still_works_once_the_code_has_run_out()
    {
        var (auth, userId, code) = await RegisteredAsync();
        using var _ = auth;

        for (var i = 0; i < MaxAttempts; i++)
            await auth.VerifyEmailWithCode.Execute(userId, Wrong(code, i));

        // The same email's link is 256 bits and nothing counts it, which is what the screen points at when the
        // code is refused — so somebody who mistyped five times is never stuck.
        Assert.That((await auth.VerifyEmail.Execute(auth.Emails.TokenFromLastLink())).Succeeded, Is.True);
    }

    [Test]
    public async Task A_new_code_comes_with_its_own_tries()
    {
        var (auth, userId, code) = await RegisteredAsync();
        using var _ = auth;

        for (var i = 0; i < MaxAttempts; i++)
            await auth.VerifyEmailWithCode.Execute(userId, Wrong(code, i));

        await auth.SendEmailVerification.Execute(await auth.Db.Users.SingleAsync());

        Assert.That((await auth.VerifyEmailWithCode.Execute(userId, auth.Emails.CodeFromLast())).Succeeded, Is.True);
    }
}
