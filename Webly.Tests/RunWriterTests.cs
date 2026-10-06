using Webly.Services.DTO.Realtime;
using Webly.Services.Services.Realtime;

namespace Webly.Tests;

/// <summary>
/// What the live screen is told when the agent's message ends. It has to be what the thread stores, and it was not:
/// the end of every turn in which the agent narrated as it worked drew that narration a second time, with the
/// <c>SUMMARY:</c> line meant for the commit under it, while a reload showed the clean answer.
/// </summary>
public class RunWriterTests
{
    [Test]
    public async Task The_completed_message_is_the_agents_reply_not_everything_it_streamed()
    {
        var sink = new RecordingSink();
        var writer = new RunWriter(sink);
        writer.Bind("run_1");

        await writer.WriteTextAsync("I'm checking the page now.\n\n");
        await writer.WriteAsync(new RunEvent { Type = RunEventType.Activity, Detail = "Reading your site" });
        await writer.WriteTextAsync("I added your opening hours.\n\nSUMMARY: Added opening hours to the home page");

        var stored = await writer.CompleteMessageAsync("I added your opening hours.");

        var completed = sink.Events.Single(e => e.Type == RunEventType.MessageCompleted);

        Assert.Multiple(() =>
        {
            Assert.That(completed.Text, Is.EqualTo("I added your opening hours."));
            Assert.That(stored, Is.EqualTo(completed.Text), "the screen is told what the thread stores");
            Assert.That(
                string.Concat(sink.Events.Where(e => e.Type == RunEventType.TextDelta).Select(e => e.Text)),
                Does.StartWith("I'm checking the page now."),
                "and the narration still streamed live, as it was written");
        });
    }

    [Test]
    public async Task Without_a_reply_the_streamed_text_is_the_message()
    {
        var sink = new RecordingSink();
        var writer = new RunWriter(sink);
        writer.Bind("run_1");

        await writer.WriteTextAsync("Half an answer");

        var stored = await writer.CompleteMessageAsync(reply: null);

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.EqualTo("Half an answer"));
            Assert.That(sink.Events.Last().Type, Is.EqualTo(RunEventType.MessageCompleted));
            Assert.That(sink.Events.Last().Text, Is.EqualTo("Half an answer"));
        });
    }

    private sealed class RecordingSink : IRunEventSink
    {
        public List<RunEvent> Events { get; } = [];

        public Task EmitAsync(
            string runId,
            RunEvent runEvent,
            bool isTerminal = false,
            CancellationToken cancellationToken = default)
        {
            Events.Add(runEvent);
            return Task.CompletedTask;
        }
    }
}
