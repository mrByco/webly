using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Webly.Data;
using Webly.Data.Models.Usage;
using Webly.Data.Repositories.Usage;

namespace Webly.Services.Services.Usage;

/// <summary>
/// A singleton with a scope of its own per write, because two of its three callers are singletons — the workspace
/// registry and the deployment runner, by way of the metered sandbox provider — and the third records after its
/// own unit of work has finished. <see cref="CancellationToken.None"/> throughout: the usual reason a turn ends
/// early is that its token tripped, and that is exactly the turn whose spend still has to be written down.
/// </summary>
public class UsageRecorder(
    IServiceScopeFactory scopeFactory,
    ILogger<UsageRecorder> logger) : IUsageRecorder
{
    private const int DescriptionLimit = 200;

    public async Task RecordAsync(UsageEntry entry)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var repository = scope.ServiceProvider.GetRequiredService<IUsageRepository>();
            var dbContext = scope.ServiceProvider.GetRequiredService<WeblyDbContext>();

            repository.Add(new UsageRecord
            {
                Kind = entry.Kind,
                Outcome = entry.Outcome,
                UserId = entry.UserId,
                SiteId = entry.SiteId,
                SiteName = Shorten(entry.SiteName),
                Agent = entry.Agent,
                Model = entry.Tokens?.Model,
                InputTokens = entry.Tokens?.InputTokens ?? 0,
                OutputTokens = entry.Tokens?.OutputTokens ?? 0,
                CacheReadTokens = entry.Tokens?.CacheReadTokens ?? 0,
                CacheWriteTokens = entry.Tokens?.CacheWriteTokens ?? 0,
                CostUsd = entry.CostUsd,
                DurationMs = entry.DurationMs,
                Description = Shorten(entry.Description)
            });

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not record {Kind} usage of {Cost} USD for user {User}; the report will be short by it.",
                entry.Kind, entry.CostUsd, entry.UserId);
        }
    }

    private static string? Shorten(string? text) =>
        text is null || text.Length <= DescriptionLimit ? text : $"{text[..(DescriptionLimit - 1)]}…";
}
