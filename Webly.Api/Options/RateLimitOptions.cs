using System.Threading.RateLimiting;

namespace Webly.Api.Options;

/// <summary>
/// How much of each limited thing one caller may do in a window, from the <c>RateLimits</c> section. The defaults
/// here are the values; nothing in <c>appsettings.json</c> repeats them.
///
/// Configuration rather than constants for two readers. Somebody loosening one during an incident should not need a
/// rebuild. And the tests send every request from one address, so a fixture of twenty tests that each register an
/// account is twenty sign-ups from one place — refused by a limit none of them is about. The test host lifts the
/// limits a fixture is not testing, and the tests that are about one keep the real thing.
/// </summary>
public class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    public FixedWindowLimit Mail { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) };

    public FixedWindowLimit SignUp { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) };

    public FixedWindowLimit SignIn { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) };

    public FixedWindowLimit Forms { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(10) };

    public FixedWindowLimit Deploy { get; set; } = new() { PermitLimit = 20, Window = TimeSpan.FromHours(1) };
}

public class FixedWindowLimit
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }

    /// <summary>No queue: a request over the limit is answered at once, rather than held open until the window turns.</summary>
    public FixedWindowRateLimiterOptions ToLimiterOptions() =>
        new() { PermitLimit = PermitLimit, Window = Window, QueueLimit = 0 };
}
