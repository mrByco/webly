using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Webly.Data.Models.Chat;
using Webly.Data.Repositories.Chat;
using Webly.Services.Agent;

namespace Webly.Services.Services.Realtime;

/// <summary>
/// Ends the threads a process death left mid-turn, once, as the app starts.
///
/// A turn lives in this process — its run, its log, its sandbox — so a restart in the middle of one ends it with
/// nobody to say so: no reply, no note, nothing on the run for a reconnecting page to find. The thread was left
/// ending in the person's own message, which reads as being ignored, and stayed that way on every reload. Found by
/// killing the backend while a turn was waking a site, which is what any deploy does to whoever is mid-sentence.
/// Every way a turn ends inside a running process writes something after that message, so at startup — when no
/// turn can be running — a thread whose last word is the person's is exactly one this happened to.
///
/// <b>Per process, like the rest of the run substrate</b> (<see cref="RunRegistry"/>, the workspace registry): with
/// two instances, one starting up would write this under a turn the other is still running. Scaling out has to
/// address this alongside those.
///
/// Best-effort: a sweep that cannot reach the database logs and lets the app start, because a missing note is
/// a smaller failure than an app that will not come up.
/// </summary>
public class InterruptedTurnSweeper(IServiceScopeFactory scopes, ILogger<InterruptedTurnSweeper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();

            var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
            var interrupted = await conversations.ListAwaitingReplyAsync(cancellationToken);

            foreach (var conversationId in interrupted)
                await conversations.AppendMessageAsync(
                    new ConversationMessage
                    {
                        ConversationId = conversationId,
                        Role = MessageRole.System,
                        Text = AgentTurnService.InterruptedNote
                    },
                    cancellationToken);

            if (interrupted.Count > 0)
                logger.LogInformation("Noted {Count} turn(s) a previous process stopped in the middle of.", interrupted.Count);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not note the turns a previous process left unfinished.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
