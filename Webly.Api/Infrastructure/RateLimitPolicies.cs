namespace Webly.Api.Infrastructure;

public static class RateLimitPolicies
{
    /// <summary>
    /// Endpoints that cause an email to be sent to an address the caller merely typed. Without a cap
    /// these are a free way to flood somebody's inbox, using our sending reputation to do it.
    /// Partitioned per IP, because the address is attacker-supplied.
    /// </summary>
    public const string Mail = "mail";

    /// <summary>
    /// Creating an account, which mails a confirmation to whatever address was typed, under a name that was typed
    /// too — so it is the same hazard as <see cref="Mail"/>, and for a long time it had no limit at all: fifteen
    /// sign-ups in a row from one address sent fifteen emails to fifteen strangers. Its own budget rather than
    /// Mail's, so the refusal can say what was done too often: "a lot of email has been asked for" is a puzzling
    /// thing to be told by a screen where you were creating an account.
    /// </summary>
    public const string SignUp = "sign-up";

    /// <summary>
    /// Signing in with a password, which is one request per guess — and there was nothing here at all: forty wrong
    /// passwords in a row against one account from one address were forty plain refusals. Per IP rather than per
    /// account, because the account is the thing the caller chooses, and a limit on it would let anybody lock
    /// anybody out by failing on purpose. What this does not stop is guessing spread across many addresses; the
    /// password hash's cost is what slows that.
    /// </summary>
    public const string SignIn = "sign-in";

    // There is deliberately no "agent" policy here. Starting a turn is a hub invocation, and the rate-limiting
    // middleware only sees HTTP endpoints — an attribute on the hub would look like a fence and be none. That budget
    // lives in AgentBudget, which the hub calls directly.

    /// <summary>
    /// Publishing. Per user: a deploy costs a provider API quota we do not own, and nothing about the
    /// product needs a site published forty times a minute. The limit is what stops a stuck client's
    /// retry loop from getting our Vercel account rate-limited for every other customer at once.
    /// </summary>
    public const string Deploy = "deploy";

    /// <summary>
    /// The form endpoint a published site posts to — the one thing here a stranger can reach, so the one thing
    /// here that is reached by people we know nothing about. Partitioned per IP <b>and per site</b>, because
    /// there is no account to partition on and the two customers a visitor writes to are unrelated: a family
    /// filling in one shop's contact form from an office must not use up the budget of everybody behind that
    /// address writing to every other shop. Generous per window, so a bot walking one form still pays for it
    /// within the minute. The per-site caps in <c>SubmitForm</c> are the other half, and they are the half a
    /// thousand hosts sending one submission each cannot get past.
    /// </summary>
    public const string Forms = "forms";
}
