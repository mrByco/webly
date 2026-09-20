using NanoidDotNet;
using System.ComponentModel;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services.Realtime;

namespace Webly.Services.Agent.Tools;

/// <summary>
/// The tool that lets the agent stop and ask. Modelled on Claude Code's own <c>AskUserQuestion</c>: the model
/// asks, the run blocks, the person picks, the run continues with the answer as the tool's result.
///
/// It is the reason the transport had to be bidirectional at all — everything else in the substrate would work
/// over a one-way stream. And it is what makes the agent safe to give real write tools: when it does not know
/// whether the bakery closes at 5 or at 6, the alternative to asking is inventing, and a plausible invention
/// published on somebody's real website is the worst thing this product can do.
/// </summary>
public class QuestionToolkit(RunRegistry registry, RunWriter writer, AgentRunContext context)
{
    /// <summary>
    /// Long enough for somebody to come back from the kitchen, short enough that a forgotten tab does not hold a
    /// run to the reaper's 30-minute cap. A timeout answers rather than throws — see below.
    /// </summary>
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromMinutes(10);

    [Description("""
        Ask the person a question and wait for their answer. Use this whenever the real answer is a fact about
        their business that you cannot know: opening hours, prices, what they actually sell, whether a photo is
        theirs. Never guess such a fact and never write a placeholder that reads like a real one. Offer options
        when there is a sensible short list; the person can always answer in their own words instead.
        """)]
    public async Task<string> AskUser(
        [Description("The question, in one sentence, in the language the person is writing in.")] string question,
        [Description("Up to four short options. Omit for an open question.")] string[]? options = null,
        CancellationToken cancellationToken = default)
    {
        var handle = registry.Get(context.RunId);

        if (handle is null) return "This run has ended, so the question could not be asked.";

        var questionId = Nanoid.Generate(size: 10);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Registered before the event goes out: an answer can arrive the instant the question renders, and a
        // registration that happened afterwards would drop it.
        handle.PendingQuestions[questionId] = pending;

        await writer.WriteAsync(new RunEvent
        {
            Type = RunEventType.QuestionAsked,
            QuestionId = questionId,
            Text = question,
            Options = options ?? []
        }, cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.Token);
        timeout.CancelAfter(AnswerTimeout);

        string answer;

        try
        {
            answer = await pending.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            handle.PendingQuestions.TryRemove(questionId, out _);

            // A result, not an exception. The model can wrap up gracefully — "I'll leave the hours out for now" —
            // where a thrown error would end the turn and lose everything it had staged.
            return "The person did not answer. Do not guess: leave that part as it is and say what you still need.";
        }

        // Emitted so that a replay shows the question resolved rather than for ever pending — a client that
        // reloads after answering must not be shown the same question again.
        await writer.WriteAsync(new RunEvent
        {
            Type = RunEventType.QuestionAnswered,
            QuestionId = questionId,
            Text = answer
        }, cancellationToken);

        return answer;
    }
}
