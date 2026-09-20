namespace Webly.Api.Infrastructure;

public static class RateLimitPolicies
{
    /// <summary>
    /// Endpoints that cause an email to be sent to an address the caller merely typed. Without a cap
    /// these are a free way to flood somebody's inbox, using our sending reputation to do it.
    /// Partitioned per IP, because the address is attacker-supplied.
    /// </summary>
    public const string Mail = "mail";

    // There is deliberately no "agent" policy here. Starting a turn is a hub invocation, and the rate-limiting
    // middleware only sees HTTP endpoints — an attribute on the hub would look like a fence and be none. That budget
    // lives in AgentBudget, which the hub calls directly.

    /// <summary>
    /// Publishing. Per user, and tighter than <see cref="Agent"/>: a deploy costs a provider API
    /// quota we do not own, and nothing about the product needs a site published forty times a
    /// minute. The limit is what stops a stuck client's retry loop from getting our Vercel account
    /// rate-limited for every other customer at once.
    /// </summary>
    public const string Deploy = "deploy";
}
