using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NanoidDotNet;
using Webly.Services.Agent;
using Webly.Services.DTO.Chat;
using Webly.Services.DTO.Realtime;

namespace Webly.Services.Services.Realtime;

/// <summary>What a caller needs in order to start following the run it just started.</summary>
public record RunStarted(string RunId, string? ConversationNanoid);

public interface IChatRunLauncher
{
    RunStarted Start(SendMessageRequest request, int userId);
}

/// <summary>
/// Owns the lifetime of an agent turn. A singleton, because the run has to outlive the hub invocation that asked
/// for it: a scoped service would be disposed the moment <c>StartChat</c> returned, taking the DbContext the agent
/// is still using with it.
///
/// Each run therefore gets <b>its own DI scope</b>, which is what makes the scoped <see cref="RunWriter"/> one per
/// run rather than one per request, and what lets the turn hold a workspace lease for its whole length.
///
/// It emits <b>exactly one terminal event on every exit path</b>, and nothing else in the codebase emits a terminal.
/// A client that never receives one waits for ever with a spinner, and that is indistinguishable from the product
/// being broken.
/// </summary>
public class ChatRunLauncher(
    IServiceScopeFactory scopeFactory,
    RunRegistry registry,
    IHostApplicationLifetime lifetime,
    ILogger<ChatRunLauncher> logger) : IChatRunLauncher
{
    public RunStarted Start(SendMessageRequest request, int userId)
    {
        var runId = $"run_{Nanoid.Generate(size: 12)}";

        // Linked to ApplicationStopping: a turn is not durable, so a deploy should end it cleanly rather than
        // leave the client waiting for a terminal event that will never come.
        var handle = registry.Register(RunKind.Chat, runId, correlationId: null, userId, lifetime.ApplicationStopping);

        _ = Task.Run(() => ExecuteAsync(runId, handle, request, userId));

        return new RunStarted(runId, null);
    }

    private async Task ExecuteAsync(string runId, RunHandle handle, SendMessageRequest request, int userId)
    {
        using var scope = scopeFactory.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        var writer = serviceProvider.GetRequiredService<RunWriter>();
        var sink = serviceProvider.GetRequiredService<IRunEventSink>();
        var turn = serviceProvider.GetRequiredService<IAgentTurnService>();

        writer.Bind(runId);

        var terminal = RunEventType.Completed;
        string? error = null;

        try
        {
            // The conversation may not exist yet, so the run is registered without a correlation id and is given
            // one as soon as the turn knows it — through the callback, not from the return value. Assigning it
            // from what `RunAsync` returns looked like the same thing and is not: that happens when the turn is
            // *over*, so for the whole time a reload could have re-attached, the registry had no way to connect
            // this run to that conversation. A page reloaded mid-turn showed a thread with nothing running and a
            // turn still writing files behind it.
            await turn.RunAsync(request, userId, nanoid => handle.CorrelationId = nanoid, handle.Token);
        }
        catch (OperationCanceledException)
        {
            // Stop, the orphan reaper, or a shutdown. All three are "the turn ended early", and the client needs a
            // terminal either way. Not Failed: nothing went wrong, and nothing was committed — the sandbox's tree
            // is replaced the next time the workspace is seeded.
            terminal = RunEventType.Completed;
            error = null;
            logger.LogInformation("Run {RunId} was cancelled.", runId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Run {RunId} failed.", runId);
            terminal = RunEventType.Failed;
            // The same sentence the turn writes into the thread, so the live screen and a reload agree.
            error = AgentTurnService.FailureNote;
        }
        finally
        {
            await FinishAsync(runId, writer, sink, terminal, error);
            registry.Finish(runId);
        }
    }

    /// <summary>
    /// The one terminal, with <see cref="CancellationToken.None"/> throughout: the usual reason to be here is that
    /// the run's own token has just tripped, and a cancelled run that cannot announce its own ending is
    /// indistinguishable from a hung one.
    /// </summary>
    private async Task FinishAsync(
        string runId,
        RunWriter writer,
        IRunEventSink sink,
        RunEventType terminal,
        string? error)
    {
        try
        {
            // Buffered text first, so the last words of a cancelled answer are not lost.
            await writer.FlushAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Flushing run {RunId} failed.", runId);
        }

        try
        {
            await sink.EmitAsync(runId, new RunEvent { Type = terminal, Error = error }, isTerminal: true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Emitting the terminal event for run {RunId} failed.", runId);
        }
    }
}
