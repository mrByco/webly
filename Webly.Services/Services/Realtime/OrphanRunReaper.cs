using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Webly.Services.DTO.Realtime;

namespace Webly.Services.Services.Realtime;

/// <summary>
/// The mandatory counterweight to "a disconnect no longer cancels a run".
///
/// Three jobs, and each one answers a way the design could otherwise leak. A run nobody has watched for two
/// minutes is cancelled — somebody closed the tab and is not coming back, and their turn is still spending our
/// provider budget. A run that has been going for half an hour is cancelled whatever its audience — that is a
/// tool loop, not a person's edit. And a finished run's handle is evicted five minutes after it ends, which is
/// the only thing keeping the in-memory log from being a memory leak with a nice name.
/// </summary>
public class OrphanRunReaper(RunRegistry registry, ILogger<OrphanRunReaper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    /// <summary>Long enough to survive a page reload and a flaky connection, short enough that a closed tab
    /// does not pay for a whole turn.</summary>
    private static readonly TimeSpan OrphanGrace = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Generous, because a turn may sit waiting for an answer to a question — a person reading a question is a
    /// subscriber, so the orphan clock does not cover them and only this cap does.
    /// </summary>
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;

            foreach (var run in registry.All)
            {
                if (run.FinishedAt is { } finishedAt)
                {
                    if (now - finishedAt > KeepFinished)
                        registry.Evict(run.RunId);

                    continue;
                }

                // A deploy is not watched by anybody once the tab is closed, and it must still finish — it is
                // durable work with an outside effect, unlike a turn. Only the lifetime cap applies to it.
                if (run.Kind == RunKind.Chat && run.SubscriberCount == 0 && now - run.UnwatchedSince > OrphanGrace)
                {
                    logger.LogInformation("Cancelling run {RunId}: nobody has watched it for {Grace}.", run.RunId, OrphanGrace);
                    registry.TryCancel(run.RunId, run.UserId);
                    continue;
                }

                if (now - run.StartedAt > MaxLifetime)
                {
                    logger.LogWarning("Cancelling run {RunId}: it has been running for over {Max}.", run.RunId, MaxLifetime);
                    registry.TryCancel(run.RunId, run.UserId);
                }
            }
        }
    }
}
