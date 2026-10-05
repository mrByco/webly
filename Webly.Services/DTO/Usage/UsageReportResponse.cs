using Webly.Data.Models.Usage;

namespace Webly.Services.DTO.Usage;

/// <summary>
/// What the platform cost over a period, for its administrators: the model's line and the machine's, in total,
/// by day, by site, by person, and the most recent turns one by one.
/// </summary>
public record UsageReportResponse
{
    public required int PeriodDays { get; init; }

    public required DateTime Since { get; init; }

    /// <summary>The rate sandbox time is priced at, so a zero machine cost can be read as "not configured".</summary>
    public required decimal SandboxCostPerHour { get; init; }

    public required UsageTotalsResponse Totals { get; init; }

    /// <summary>Every day of the period, oldest first, including the ones that cost nothing.</summary>
    public required IReadOnlyList<UsageDayResponse> ByDay { get; init; }

    /// <summary>Most expensive first.</summary>
    public required IReadOnlyList<UsageSiteResponse> BySite { get; init; }

    /// <summary>Most expensive first.</summary>
    public required IReadOnlyList<UsageUserResponse> ByUser { get; init; }

    /// <summary>Newest first.</summary>
    public required IReadOnlyList<UsageTurnResponse> RecentTurns { get; init; }
}

public record UsageTotalsResponse
{
    public required decimal ModelCostUsd { get; init; }

    public required decimal SandboxCostUsd { get; init; }

    public required int Turns { get; init; }

    /// <summary>Turns that failed or were stopped — paid for, and produced nothing.</summary>
    public required int UnfinishedTurns { get; init; }

    public required long InputTokens { get; init; }

    public required long OutputTokens { get; init; }

    public required long CacheReadTokens { get; init; }

    public required long CacheWriteTokens { get; init; }

    public required long SandboxSeconds { get; init; }
}

public record UsageDayResponse
{
    public required DateOnly Day { get; init; }

    public required decimal ModelCostUsd { get; init; }

    public required decimal SandboxCostUsd { get; init; }

    public required int Turns { get; init; }
}

public record UsageSiteResponse
{
    /// <summary>Null once the site has been deleted; what it cost stays in the report.</summary>
    public string? SiteNanoid { get; init; }

    public required string SiteName { get; init; }

    public required string OwnerEmail { get; init; }

    public required decimal ModelCostUsd { get; init; }

    public required decimal SandboxCostUsd { get; init; }

    public required int Turns { get; init; }

    public required long SandboxSeconds { get; init; }
}

public record UsageUserResponse
{
    public required string UserNanoid { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public required decimal ModelCostUsd { get; init; }

    public required decimal SandboxCostUsd { get; init; }

    public required int Turns { get; init; }

    public required long SandboxSeconds { get; init; }
}

public record UsageTurnResponse
{
    public required string Nanoid { get; init; }

    public required DateTime CreatedAt { get; init; }

    public string? SiteNanoid { get; init; }

    public required string SiteName { get; init; }

    public required string OwnerEmail { get; init; }

    public string? Agent { get; init; }

    public string? Model { get; init; }

    public required UsageOutcome Outcome { get; init; }

    public required long InputTokens { get; init; }

    public required long OutputTokens { get; init; }

    public required long CacheReadTokens { get; init; }

    public required long CacheWriteTokens { get; init; }

    public required decimal CostUsd { get; init; }

    public required long DurationMs { get; init; }

    /// <summary>The start of what the person asked for.</summary>
    public string? Description { get; init; }
}
