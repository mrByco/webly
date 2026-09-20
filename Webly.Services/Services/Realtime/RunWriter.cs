using System.Text;
using Webly.Services.DTO.Realtime;

namespace Webly.Services.Services.Realtime;

/// <summary>
/// What a run writes its output through. Scoped, and bound to exactly one run — a run gets its own DI scope
/// (see <see cref="ChatRunLauncher"/>), which is what makes "one bound run per scope" true rather than
/// hopeful.
///
/// Its one real job is <b>coalescing text</b>. A model streams a few characters at a time; a SignalR message
/// per token is thousands of messages per turn, and the client repaints on every one. Buffering to 200
/// characters or 100 milliseconds turns that into a readable stream at a fraction of the traffic.
///
/// The ordering rule is the part that is easy to get wrong: <b>flush before any non-text event</b>. Otherwise a
/// tool-call chip arrives before the sentence that introduced it, and the transcript reads backwards.
/// </summary>
public sealed class RunWriter(IRunEventSink sink)
{
    private const int FlushAtChars = 200;
    private static readonly TimeSpan FlushAfter = TimeSpan.FromMilliseconds(100);

    private readonly StringBuilder _buffer = new();
    private readonly StringBuilder _message = new();
    private DateTime _lastFlush = DateTime.UtcNow;

    private string? _runId;

    /// <summary>Called once, by the launcher, before anything is written.</summary>
    public void Bind(string runId) => _runId = runId;

    private string RunId => _runId
        ?? throw new InvalidOperationException("This RunWriter was used before it was bound to a run.");

    /// <summary>A piece of the model's answer. Buffered; may or may not go out now.</summary>
    public async Task WriteTextAsync(string text, CancellationToken cancellationToken = default)
    {
        _buffer.Append(text);
        _message.Append(text);

        if (_buffer.Length >= FlushAtChars || DateTime.UtcNow - _lastFlush >= FlushAfter)
            await FlushAsync(cancellationToken);
    }

    /// <summary>Anything that is not text. Flushes first, so order is preserved.</summary>
    public async Task WriteAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken);
        await sink.EmitAsync(RunId, runEvent, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Ends the assistant's message and returns the whole of it, for persisting. The complete text is sent as
    /// well as the deltas: a client that joined mid-turn has the tail but not the head, and replaying deltas to
    /// rebuild a message is more fragile than being told what it says.
    /// </summary>
    public async Task<string> CompleteMessageAsync(CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken);

        var text = _message.ToString();
        _message.Clear();

        if (text.Length > 0)
            await sink.EmitAsync(RunId, new RunEvent { Type = RunEventType.MessageCompleted, Text = text },
                cancellationToken: cancellationToken);

        return text;
    }

    /// <summary>
    /// Sends whatever is buffered. Public because the launcher calls it on the way out: the usual reason to be
    /// finishing is that the run was cancelled, and the last words of a cancelled answer are still words the
    /// person was reading.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_buffer.Length == 0) return;

        var text = _buffer.ToString();
        _buffer.Clear();
        _lastFlush = DateTime.UtcNow;

        await sink.EmitAsync(RunId, new RunEvent { Type = RunEventType.TextDelta, Text = text },
            cancellationToken: cancellationToken);
    }
}
