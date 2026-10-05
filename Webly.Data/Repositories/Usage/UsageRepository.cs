using Microsoft.EntityFrameworkCore;
using Webly.Data.Models.Usage;

namespace Webly.Data.Repositories.Usage;

public class UsageRepository(WeblyDbContext dbContext) : IUsageRepository
{
    public void Add(UsageRecord record) => dbContext.UsageRecords.Add(record);

    private IQueryable<UsageRecord> Since(DateTime since) =>
        dbContext.UsageRecords.AsNoTracking().Where(x => x.CreatedAt >= since);

    public async Task<UsageTotalsRow> TotalsAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        // Grouped by a constant so the whole period is one row, worked out by the database in one statement.
        var totals = await Since(since)
            .GroupBy(_ => 1)
            .Select(g => new UsageTotalsRow(
                g.Sum(x => x.Kind == UsageKind.AgentTurn ? x.CostUsd : 0),
                g.Sum(x => x.Kind != UsageKind.AgentTurn ? x.CostUsd : 0),
                g.Count(x => x.Kind == UsageKind.AgentTurn),
                g.Count(x => x.Kind == UsageKind.AgentTurn && x.Outcome != UsageOutcome.Completed),
                g.Sum(x => x.InputTokens),
                g.Sum(x => x.OutputTokens),
                g.Sum(x => x.CacheReadTokens),
                g.Sum(x => x.CacheWriteTokens),
                g.Sum(x => x.Kind != UsageKind.AgentTurn ? x.DurationMs : 0)))
            .FirstOrDefaultAsync(cancellationToken);

        return totals ?? new UsageTotalsRow(0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    public async Task<List<UsageDayRow>> ByDayAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        var days = await Since(since)
            .GroupBy(x => x.CreatedAt.Date)
            .Select(g => new
            {
                Day = g.Key,
                ModelCost = g.Sum(x => x.Kind == UsageKind.AgentTurn ? x.CostUsd : 0),
                SandboxCost = g.Sum(x => x.Kind != UsageKind.AgentTurn ? x.CostUsd : 0),
                Turns = g.Count(x => x.Kind == UsageKind.AgentTurn)
            })
            .OrderBy(x => x.Day)
            .ToListAsync(cancellationToken);

        return [.. days.Select(x => new UsageDayRow(DateOnly.FromDateTime(x.Day), x.ModelCost, x.SandboxCost, x.Turns))];
    }

    public async Task<List<UsageSiteRow>> BySiteAsync(DateTime since, int take, CancellationToken cancellationToken = default)
    {
        // Grouped by the snapshot name as well as the id, so a deleted site — whose id is now null — still has a
        // row of its own rather than being pooled with every other deleted site.
        var groups = await Since(since)
            .GroupBy(x => new { x.SiteId, x.SiteName, x.UserId })
            .Select(g => new
            {
                g.Key.SiteId,
                g.Key.SiteName,
                g.Key.UserId,
                ModelCost = g.Sum(x => x.Kind == UsageKind.AgentTurn ? x.CostUsd : 0),
                SandboxCost = g.Sum(x => x.Kind != UsageKind.AgentTurn ? x.CostUsd : 0),
                Turns = g.Count(x => x.Kind == UsageKind.AgentTurn),
                SandboxMs = g.Sum(x => x.Kind != UsageKind.AgentTurn ? x.DurationMs : 0)
            })
            .OrderByDescending(x => x.ModelCost + x.SandboxCost)
            .ThenByDescending(x => x.SandboxMs)
            .Take(take)
            .ToListAsync(cancellationToken);

        var siteIds = groups.Where(x => x.SiteId is not null).Select(x => x.SiteId!.Value).Distinct().ToList();
        var userIds = groups.Select(x => x.UserId).Distinct().ToList();

        // The current name and nanoid for a site that still exists: a rename since the spend should show the name
        // somebody would recognise now.
        var sites = await dbContext.Sites.AsNoTracking()
            .Where(x => siteIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Nanoid, x.Name }, cancellationToken);

        var emails = await dbContext.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Email, cancellationToken);

        return [.. groups.Select(x =>
        {
            var site = x.SiteId is { } id && sites.TryGetValue(id, out var found) ? found : null;

            return new UsageSiteRow(
                site?.Nanoid,
                site?.Name ?? x.SiteName ?? "(unknown site)",
                emails.GetValueOrDefault(x.UserId, string.Empty),
                x.ModelCost,
                x.SandboxCost,
                x.Turns,
                x.SandboxMs);
        })];
    }

    public async Task<List<UsageUserRow>> ByUserAsync(DateTime since, int take, CancellationToken cancellationToken = default)
    {
        var groups = await Since(since)
            .GroupBy(x => x.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                ModelCost = g.Sum(x => x.Kind == UsageKind.AgentTurn ? x.CostUsd : 0),
                SandboxCost = g.Sum(x => x.Kind != UsageKind.AgentTurn ? x.CostUsd : 0),
                Turns = g.Count(x => x.Kind == UsageKind.AgentTurn),
                SandboxMs = g.Sum(x => x.Kind != UsageKind.AgentTurn ? x.DurationMs : 0)
            })
            .OrderByDescending(x => x.ModelCost + x.SandboxCost)
            .ThenByDescending(x => x.SandboxMs)
            .Take(take)
            .ToListAsync(cancellationToken);

        var userIds = groups.Select(x => x.UserId).ToList();
        var users = await dbContext.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Nanoid, x.Email, x.DisplayName }, cancellationToken);

        return [.. groups
            .Where(x => users.ContainsKey(x.UserId))
            .Select(x => new UsageUserRow(
                users[x.UserId].Nanoid,
                users[x.UserId].Email,
                users[x.UserId].DisplayName,
                x.ModelCost,
                x.SandboxCost,
                x.Turns,
                x.SandboxMs))];
    }

    public async Task<List<UsageTurnRow>> RecentTurnsAsync(DateTime since, int take, CancellationToken cancellationToken = default) =>
        await Since(since)
            .Where(x => x.Kind == UsageKind.AgentTurn)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new UsageTurnRow(
                x.Nanoid,
                x.CreatedAt,
                x.Site != null ? x.Site.Nanoid : null,
                x.Site != null ? x.Site.Name : x.SiteName ?? "(unknown site)",
                x.User.Email,
                x.Agent,
                x.Model,
                x.Outcome,
                x.InputTokens,
                x.OutputTokens,
                x.CacheReadTokens,
                x.CacheWriteTokens,
                x.CostUsd,
                x.DurationMs,
                x.Description))
            .ToListAsync(cancellationToken);
}
