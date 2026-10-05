using Microsoft.Extensions.Options;
using Webly.Data.Repositories.Usage;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Usage;
using Webly.Services.Services.Authentication;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.UseCases.Usage;

/// <summary>
/// What the platform cost over the last <c>days</c> days, for an administrator.
///
/// Platform-wide and administrators only, deliberately: this is what Webly pays, and what a customer is charged
/// is a plan — a different number, and a decision for when there are plans (MASTER_PLAN P7). Showing a customer
/// the model's bill for their turn would also tell them which model edits their site, which the product never does.
/// </summary>
public class GetUsageReport(
    IAdminPolicy adminPolicy,
    IUsageRepository usage,
    IOptions<SandboxOptions> sandboxOptions)
{
    /// <summary>A screen's worth; the totals and the groups cover the whole period regardless.</summary>
    public const int Take = 50;

    public async Task<Result<UsageError, UsageReportResponse>> ExecuteAsync(
        int userId,
        int days,
        CancellationToken cancellationToken = default)
    {
        if (!await adminPolicy.IsAdminAsync(userId, cancellationToken))
            return Result<UsageError, UsageReportResponse>.Fail(UsageError.NotAnAdministrator);

        days = Math.Clamp(days, 1, 366);

        // Whole days, so "the last 7 days" is seven bars that each mean a day rather than a rolling 168 hours.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstDay = today.AddDays(1 - days);
        var since = firstDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var totals = await usage.TotalsAsync(since, cancellationToken);
        var byDay = (await usage.ByDayAsync(since, cancellationToken)).ToDictionary(x => x.Day);
        var bySite = await usage.BySiteAsync(since, Take, cancellationToken);
        var byUser = await usage.ByUserAsync(since, Take, cancellationToken);
        var recent = await usage.RecentTurnsAsync(since, Take, cancellationToken);

        return Result<UsageError, UsageReportResponse>.Ok(new UsageReportResponse
        {
            PeriodDays = days,
            Since = since,
            SandboxCostPerHour = sandboxOptions.Value.CostPerHour,
            Totals = new UsageTotalsResponse
            {
                ModelCostUsd = totals.ModelCostUsd,
                SandboxCostUsd = totals.SandboxCostUsd,
                Turns = totals.Turns,
                UnfinishedTurns = totals.UnfinishedTurns,
                InputTokens = totals.InputTokens,
                OutputTokens = totals.OutputTokens,
                CacheReadTokens = totals.CacheReadTokens,
                CacheWriteTokens = totals.CacheWriteTokens,
                SandboxSeconds = totals.SandboxMs / 1000
            },
            // Every day, the empty ones included: a gap in a row of bars is a fact about the period, and leaving the
            // day out would draw a quiet Tuesday as if it had not happened.
            ByDay =
            [
                .. Enumerable.Range(0, days).Select(offset =>
                {
                    var day = firstDay.AddDays(offset);
                    var row = byDay.GetValueOrDefault(day);

                    return new UsageDayResponse
                    {
                        Day = day,
                        ModelCostUsd = row?.ModelCostUsd ?? 0,
                        SandboxCostUsd = row?.SandboxCostUsd ?? 0,
                        Turns = row?.Turns ?? 0
                    };
                })
            ],
            BySite =
            [
                .. bySite.Select(x => new UsageSiteResponse
                {
                    SiteNanoid = x.SiteNanoid,
                    SiteName = x.SiteName,
                    OwnerEmail = x.OwnerEmail,
                    ModelCostUsd = x.ModelCostUsd,
                    SandboxCostUsd = x.SandboxCostUsd,
                    Turns = x.Turns,
                    SandboxSeconds = x.SandboxMs / 1000
                })
            ],
            ByUser =
            [
                .. byUser.Select(x => new UsageUserResponse
                {
                    UserNanoid = x.UserNanoid,
                    Email = x.Email,
                    DisplayName = x.DisplayName,
                    ModelCostUsd = x.ModelCostUsd,
                    SandboxCostUsd = x.SandboxCostUsd,
                    Turns = x.Turns,
                    SandboxSeconds = x.SandboxMs / 1000
                })
            ],
            RecentTurns =
            [
                .. recent.Select(x => new UsageTurnResponse
                {
                    Nanoid = x.Nanoid,
                    CreatedAt = x.CreatedAt,
                    SiteNanoid = x.SiteNanoid,
                    SiteName = x.SiteName,
                    OwnerEmail = x.OwnerEmail,
                    Agent = x.Agent,
                    Model = x.Model,
                    Outcome = x.Outcome,
                    InputTokens = x.InputTokens,
                    OutputTokens = x.OutputTokens,
                    CacheReadTokens = x.CacheReadTokens,
                    CacheWriteTokens = x.CacheWriteTokens,
                    CostUsd = x.CostUsd,
                    DurationMs = x.DurationMs,
                    Description = x.Description
                })
            ]
        });
    }
}
