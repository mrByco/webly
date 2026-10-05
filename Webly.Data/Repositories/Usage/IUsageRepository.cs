using Webly.Data.Models.Usage;

namespace Webly.Data.Repositories.Usage;

/// <summary>
/// What things cost. Written by one recorder and read by one report; every read is an aggregate over a period,
/// worked out by the database rather than by loading the rows, because a busy month is tens of thousands of them.
/// </summary>
public interface IUsageRepository
{
    void Add(UsageRecord record);

    Task<UsageTotalsRow> TotalsAsync(DateTime since, CancellationToken cancellationToken = default);

    Task<List<UsageDayRow>> ByDayAsync(DateTime since, CancellationToken cancellationToken = default);

    Task<List<UsageSiteRow>> BySiteAsync(DateTime since, int take, CancellationToken cancellationToken = default);

    Task<List<UsageUserRow>> ByUserAsync(DateTime since, int take, CancellationToken cancellationToken = default);

    Task<List<UsageTurnRow>> RecentTurnsAsync(DateTime since, int take, CancellationToken cancellationToken = default);
}

public record UsageTotalsRow(
    decimal ModelCostUsd,
    decimal SandboxCostUsd,
    int Turns,
    int UnfinishedTurns,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long SandboxMs);

public record UsageDayRow(DateOnly Day, decimal ModelCostUsd, decimal SandboxCostUsd, int Turns);

/// <summary>A site's spend. <see cref="SiteNanoid"/> is null once the site has been deleted.</summary>
public record UsageSiteRow(
    string? SiteNanoid,
    string SiteName,
    string OwnerEmail,
    decimal ModelCostUsd,
    decimal SandboxCostUsd,
    int Turns,
    long SandboxMs);

public record UsageUserRow(
    string UserNanoid,
    string Email,
    string DisplayName,
    decimal ModelCostUsd,
    decimal SandboxCostUsd,
    int Turns,
    long SandboxMs);

public record UsageTurnRow(
    string Nanoid,
    DateTime CreatedAt,
    string? SiteNanoid,
    string SiteName,
    string OwnerEmail,
    string? Agent,
    string? Model,
    UsageOutcome Outcome,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    decimal CostUsd,
    long DurationMs,
    string? Description);
