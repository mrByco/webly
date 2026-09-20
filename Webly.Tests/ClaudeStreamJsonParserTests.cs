using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using Webly.Services.Agent;
using Webly.Services.Agent.Agents;
using Webly.Services.Services.Sandboxes;

namespace Webly.Tests;

/// <summary>
/// The parser, against a real recorded turn.
///
/// <c>Fixtures/claude-stream-json.ndjson</c> is the actual output of
/// <c>claude -p --output-format stream-json --verbose</c> editing the starter template inside a real sandbox,
/// recorded by <c>tools/e2e/run.mjs</c> and sanitised of the recorder's own identifiers. That matters more
/// than the usual argument for a fixture: this is the one class in the solution whose input format belongs to
/// another product, so a hand-written sample would only prove that the parser agrees with our guess about it.
///
/// Re-record it by running <c>node tools/e2e/run.mjs --agent claude</c> with the CLI on PATH.
/// </summary>
public class ClaudeStreamJsonParserTests
{
    private static string Transcript =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "claude-stream-json.ndjson"));

    /// <summary>Feeds the transcript in chunks of a given size, the way a streaming process would.</summary>
    private static async Task<(ClaudeStreamJsonParser Parser, List<CodingAgentEvent> Events)> ReadAsync(int chunkSize)
    {
        var events = new List<CodingAgentEvent>();
        var parser = new ClaudeStreamJsonParser(
            @event =>
            {
                events.Add(@event);
                return Task.CompletedTask;
            },
            NullLogger.Instance);

        var text = Transcript;

        for (var index = 0; index < text.Length; index += chunkSize)
        {
            var slice = text.Substring(index, Math.Min(chunkSize, text.Length - index));
            await parser.HandleAsync(new SandboxOutput(IsError: false, Text: slice));
        }

        return (parser, events);
    }

    [Test]
    public async Task It_reads_the_session_id_off_the_envelope()
    {
        var (parser, _) = await ReadAsync(4096);

        // Not inside `message` — the recorded stream puts it on every event, which is what the code relies on.
        Assert.That(parser.SessionId, Is.EqualTo("00000000-0000-4000-8000-000000000000"));
    }

    [Test]
    public async Task The_reply_is_the_final_result_not_the_narration()
    {
        var (parser, _) = await ReadAsync(4096);

        Assert.Multiple(() =>
        {
            Assert.That(parser.Failed, Is.False);

            // The streamed text blocks include mid-turn narration; the final result event does not. Taking the
            // result is what stops the chat showing "now let me update brand.md" as part of the answer.
            Assert.That(parser.Reply, Does.StartWith("The change is a small, low-risk text edit"));
            Assert.That(parser.Reply, Does.Contain("Updated the home page hero"));
        });
    }

    /// <summary>
    /// The recorded turn has thinking blocks interleaved with its text, and a person asked for a headline
    /// rather than for the reasoning. Asserted by rebuilding the expected stream from the fixture and
    /// comparing, so this fails if a future parser starts forwarding a block kind it should not.
    /// </summary>
    [Test]
    public async Task Only_text_blocks_are_streamed_to_the_person()
    {
        var (_, events) = await ReadAsync(4096);

        var expected = new List<string>();
        var thinking = 0;

        foreach (var line in Transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(line);
            if (node?["type"]?.GetValue<string>() != "assistant") continue;

            foreach (var block in node["message"]?["content"]?.AsArray() ?? [])
            {
                switch (block?["type"]?.GetValue<string>())
                {
                    case "text": expected.Add(block["text"]!.GetValue<string>()); break;
                    case "thinking": thinking++; break;
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(thinking, Is.GreaterThan(0), "the fixture does contain thinking blocks to exclude");
            Assert.That(events.OfType<CodingAgentEvent.Text>().Select(x => x.Value), Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task Every_edited_file_is_reported_once_relative_to_the_site()
    {
        var (_, events) = await ReadAsync(4096);

        var paths = events.OfType<CodingAgentEvent.FileChanged>().Select(x => x.Path).ToList();

        Assert.Multiple(() =>
        {
            // Exactly the two files the turn edited, with /workspace/ stripped: these are what the editor
            // lists under the turn, and what a stray absolute path would make unreadable.
            Assert.That(paths, Is.EquivalentTo(new[] { "content/brand.md", "src/app/page.tsx" }));

            // Read and Bash are activity, never a file change — reporting a read as a write would tell
            // somebody their site changed when it did not.
            Assert.That(events.OfType<CodingAgentEvent.Activity>().Select(x => x.Phrase),
                Does.Contain("Reading your site").And.Contain("Running a command"));
        });
    }

    [Test]
    public async Task Activity_is_phrased_for_a_person_never_as_a_tool_name()
    {
        var (_, events) = await ReadAsync(4096);
        var phrases = events.OfType<CodingAgentEvent.Activity>().Select(x => x.Phrase).Distinct().ToList();

        Assert.That(phrases, Has.All.Matches<string>(phrase =>
            !phrase.Equals("Bash") && !phrase.Equals("Read") && !phrase.Equals("Edit")),
            $"a tool name reached the UI: {string.Join(", ", phrases)}");
    }

    [Test]
    public async Task The_summary_convention_is_honoured_and_stripped()
    {
        var (parser, _) = await ReadAsync(4096);
        var outcome = parser.ToOutcome("the person's own message");

        Assert.Multiple(() =>
        {
            // The recorded turn followed the convention the prompt asks for. This is the assertion that says
            // the commit subject is the agent's sentence rather than a fallback.
            Assert.That(outcome.Summary, Is.EqualTo("Set home page hero to Koopman Cycles, bike repair shop in Utrecht"));
            Assert.That(outcome.Summary.Length, Is.LessThanOrEqualTo(70));

            // And it is removed from what the person reads: they do not need to see the commit message.
            Assert.That(outcome.Reply, Does.Not.Contain("SUMMARY:"));
            Assert.That(outcome.Reply, Does.Contain("Updated the home page hero"));
            Assert.That(outcome.SessionId, Is.EqualTo("00000000-0000-4000-8000-000000000000"));
        });
    }

    /// <summary>
    /// The line buffering, which is the part most likely to be subtly wrong: a chunk boundary can land
    /// anywhere, including mid-object and mid-multibyte-character, and the result must not depend on it.
    /// </summary>
    [TestCase(1)]
    [TestCase(7)]
    [TestCase(512)]
    [TestCase(1_000_000)]
    public async Task The_result_does_not_depend_on_where_the_chunks_break(int chunkSize)
    {
        var (parser, events) = await ReadAsync(chunkSize);
        var outcome = parser.ToOutcome("fallback");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Summary, Is.EqualTo("Set home page hero to Koopman Cycles, bike repair shop in Utrecht"));
            Assert.That(events.OfType<CodingAgentEvent.FileChanged>().Select(x => x.Path),
                Is.EquivalentTo(new[] { "content/brand.md", "src/app/page.tsx" }));
        });
    }

    [Test]
    public async Task Stderr_is_never_content()
    {
        var events = new List<CodingAgentEvent>();
        var parser = new ClaudeStreamJsonParser(@event =>
        {
            events.Add(@event);
            return Task.CompletedTask;
        }, NullLogger.Instance);

        await parser.HandleAsync(new SandboxOutput(IsError: true, Text: "(node:42) ExperimentalWarning: something\n"));

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.Empty);
            Assert.That(parser.Reply, Is.Empty);
        });
    }

    [Test]
    public async Task A_failed_result_is_not_shown_as_a_reply()
    {
        var parser = new ClaudeStreamJsonParser(_ => Task.CompletedTask, NullLogger.Instance);

        await parser.HandleAsync(new SandboxOutput(IsError: false, Text:
            """{"type":"assistant","session_id":"s","message":{"content":[{"type":"text","text":"I started to "}]}}""" + "\n"
            + """{"type":"result","subtype":"error_during_execution","is_error":true,"result":"Error: the model refused","session_id":"s"}""" + "\n"));

        Assert.Multiple(() =>
        {
            Assert.That(parser.Failed, Is.True);

            // The error text is the launcher's business, not the chat's: replacing the reply with it would
            // show "Error: …" as though the agent had answered.
            Assert.That(parser.Reply, Is.EqualTo("I started to "));
        });
    }

    [Test]
    public async Task An_unrecognised_event_costs_nothing()
    {
        var events = new List<CodingAgentEvent>();
        var parser = new ClaudeStreamJsonParser(@event =>
        {
            events.Add(@event);
            return Task.CompletedTask;
        }, NullLogger.Instance);

        // Three real event types from the recorded stream that this deliberately ignores, a line that is not
        // JSON at all, and one whose `type` is the wrong JSON type entirely — the CLI's stream is somebody
        // else's interface, and none of these may cost a turn's work.
        await parser.HandleAsync(new SandboxOutput(IsError: false, Text:
            """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed"},"session_id":"s"}""" + "\n"
            + """{"type":"autocompact_state","value":{"enabled":true},"session_id":"s"}""" + "\n"
            + """{"type":"user","message":{"content":[{"type":"tool_result","content":"ok"}]},"session_id":"s"}""" + "\n"
            + """{"type":42,"session_id":"s"}""" + "\n"
            + "not json at all\n"));

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.Empty);
            Assert.That(parser.SessionId, Is.EqualTo("s"), "the envelope's session id is still read");
            Assert.That(parser.Failed, Is.False);
        });
    }
}
