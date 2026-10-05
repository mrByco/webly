using System.ComponentModel.DataAnnotations;
using Webly.Data.Models.Authentication;
using Webly.Data.Models.Interfaces;
using Webly.Data.Models.Sites;

namespace Webly.Data.Models.Usage;

/// <summary>
/// One thing that cost money: an agent turn's model calls, or a sandbox's running time.
///
/// The product's unit cost has two lines — the model and the machine — and both are recorded here as they are
/// spent, so that "what does this site cost us" is a query rather than an estimate. An agent turn's numbers are
/// what the agent's own CLI reported (both compute cost from the provider's list prices); a sandbox's are its
/// lifetime, priced by <c>Sandbox:CostPerHour</c>. A turn that failed or was stopped is recorded too, because it
/// spent the money anyway — a model that ran out of credit halfway through still billed the first half.
/// </summary>
public class UsageRecord : IHasNanoid, IHasCreatedAt
{
    [Key]
    public int Id { get; set; }

    public string Nanoid { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public UsageKind Kind { get; set; }

    public UsageOutcome Outcome { get; set; }

    /// <summary>Whose spend it is: the site's owner. Closing an account removes it with everything else.</summary>
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// The site, while it exists. Deleting a site does not un-spend what it cost, so the row stays and this goes
    /// null — and <see cref="SiteName"/> is what the report shows for it afterwards.
    /// </summary>
    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    [MaxLength(200)]
    public string? SiteName { get; set; }

    /// <summary><c>claude-code</c>, <c>opencode</c>, <c>mock</c>; null for a sandbox.</summary>
    [MaxLength(40)]
    public string? Agent { get; set; }

    [MaxLength(100)]
    public string? Model { get; set; }

    /// <summary>Input tokens that were not read from the cache, as both providers count them.</summary>
    public long InputTokens { get; set; }

    /// <summary>Output tokens, reasoning included.</summary>
    public long OutputTokens { get; set; }

    public long CacheReadTokens { get; set; }

    public long CacheWriteTokens { get; set; }

    public decimal CostUsd { get; set; }

    /// <summary>A turn from the message arriving to its ending; a sandbox from starting to stopping.</summary>
    public long DurationMs { get; set; }

    /// <summary>What was asked, for a turn — the start of the person's message — so the report says what a cost was for.</summary>
    [MaxLength(200)]
    public string? Description { get; set; }
}
