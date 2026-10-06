using Webly.Services.Agent;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services.Realtime;

namespace Webly.Tests;

/// <summary>
/// Who ended a turn early decides the sentence the thread ends on. All of them used to end it with "Stopped. Nothing
/// was changed." — including a deploy of Webly, which reads as the person having pressed Stop.
/// </summary>
public class CancelReasonTests
{
    [Test]
    public void Stop_and_the_reaper_say_who_they_are()
    {
        var registry = new RunRegistry();

        var stopped = registry.Register(RunKind.Chat, "run_stopped", null, userId: 1);
        var abandoned = registry.Register(RunKind.Chat, "run_abandoned", null, userId: 1);
        var overran = registry.Register(RunKind.Chat, "run_overran", null, userId: 1);

        registry.TryCancel("run_stopped", 1);
        registry.TryCancel("run_abandoned", 1, CancelReason.Unwatched);
        registry.TryCancel("run_overran", 1, CancelReason.TooLong);

        Assert.Multiple(() =>
        {
            Assert.That(stopped.CancelledBecause, Is.EqualTo(CancelReason.Stopped), "Stop is the default reason");
            Assert.That(abandoned.CancelledBecause, Is.EqualTo(CancelReason.Unwatched));
            Assert.That(overran.CancelledBecause, Is.EqualTo(CancelReason.TooLong));
            Assert.That(stopped.Token.IsCancellationRequested && abandoned.Token.IsCancellationRequested, Is.True);
        });
    }

    /// <summary>A shutdown reaches the run through the linked token, and nobody giving a reason is what marks it.</summary>
    [Test]
    public void A_shutdown_gives_no_reason_and_reads_as_an_interruption()
    {
        using var stopping = new CancellationTokenSource();
        var handle = new RunRegistry().Register(RunKind.Chat, "run_deploy", null, userId: 1, stopping.Token);

        stopping.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(handle.Token.IsCancellationRequested, Is.True);
            Assert.That(handle.CancelledBecause, Is.Null);
            Assert.That(AgentTurnService.CancelledNote(handle.CancelledBecause), Is.EqualTo(AgentTurnService.InterruptedNote));
        });
    }

    [Test]
    public void The_first_reason_stands()
    {
        var registry = new RunRegistry();
        var handle = registry.Register(RunKind.Chat, "run_both", null, userId: 1);

        registry.TryCancel("run_both", 1);
        registry.TryCancel("run_both", 1, CancelReason.Unwatched);

        Assert.That(handle.CancelledBecause, Is.EqualTo(CancelReason.Stopped), "a Stop a moment before the reaper looked");
    }

    [Test]
    public void Each_ending_has_its_own_sentence()
    {
        var sentences = new[] { CancelReason.Stopped, CancelReason.Unwatched, CancelReason.TooLong }
            .Select(reason => AgentTurnService.CancelledNote(reason))
            .Append(AgentTurnService.CancelledNote(null))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sentences, Is.Unique);
            Assert.That(sentences[0], Is.EqualTo(AgentTurnService.StoppedNote));
            Assert.That(sentences.Skip(1), Has.All.Contains("Nothing was changed"));
        });
    }
}
