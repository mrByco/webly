using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using Webly.Services.Agent;
using Webly.Services.Agent.Agents;
using Webly.Services.Services.Sandboxes;

namespace Webly.Tests;

/// <summary>
/// <c>opencode run --format json</c>, against a real recording: a turn on the starter template, on
/// <c>openai/gpt-5.5</c>, with the recording machine's paths replaced by <c>/workspace</c>. The expected values are
/// read out of the same file rather than written down, so a re-recording is a new truth rather than a red test.
/// </summary>
public class OpenCodeJsonParserTests
{
    private static List<JsonNode> Lines =>
    [
        .. File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "opencode-json.ndjson"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line))
            .OfType<JsonNode>()
    ];

    private static IEnumerable<JsonNode> OfType(string type) => Lines.Where(x => x["type"]?.GetValue<string>() == type);

    private static async Task<(OpenCodeJsonParser Parser, List<CodingAgentEvent> Events)> ReadAsync(int chunkSize)
    {
        var events = new List<CodingAgentEvent>();
        var parser = new OpenCodeJsonParser("openai/gpt-5.5", @event =>
        {
            events.Add(@event);
            return Task.CompletedTask;
        }, NullLogger.Instance);

        var text = string.Join('\n', Lines.Select(x => x.ToJsonString())) + "\n";

        for (var index = 0; index < text.Length; index += chunkSize)
            await parser.HandleAsync(new SandboxOutput(IsError: false, text.Substring(index, Math.Min(chunkSize, text.Length - index))));

        return (parser, events);
    }

    [TestCase(13)]
    [TestCase(4096)]
    public async Task What_a_turn_cost_is_the_sum_of_its_steps(int chunkSize)
    {
        var (_, events) = await ReadAsync(chunkSize);

        var steps = OfType("step_finish").Select(x => x["part"]!).ToList();
        var reported = events.OfType<CodingAgentEvent.Usage>()
            .Aggregate(AgentUsage.None, (total, usage) => total.Add(usage.Value));

        Assert.Multiple(() =>
        {
            Assert.That(events.OfType<CodingAgentEvent.Usage>().Count(), Is.EqualTo(steps.Count), "one report per model call");
            Assert.That(reported.CostUsd, Is.EqualTo(steps.Sum(x => x["cost"]!.GetValue<decimal>())));
            Assert.That(reported.CostUsd, Is.GreaterThan(0m), "and a real turn is not free");
            Assert.That(reported.InputTokens, Is.EqualTo(steps.Sum(x => x["tokens"]!["input"]!.GetValue<long>())));
            Assert.That(
                reported.OutputTokens,
                Is.EqualTo(steps.Sum(x => x["tokens"]!["output"]!.GetValue<long>() + x["tokens"]!["reasoning"]!.GetValue<long>())),
                "output with the reasoning that is billed as output");
            Assert.That(reported.CacheReadTokens, Is.EqualTo(steps.Sum(x => x["tokens"]!["cache"]!["read"]!.GetValue<long>())));
            Assert.That(reported.Model, Is.EqualTo("openai/gpt-5.5"));
        });
    }

    [Test]
    public async Task The_reply_is_the_answer_and_not_the_narration_before_it()
    {
        var (parser, events) = await ReadAsync(4096);

        var lastStep = OfType("step_finish").Last()["part"]!["messageID"]!.GetValue<string>();
        var texts = OfType("text").Select(x => x["part"]!).ToList();
        var answer = texts.Where(x => x["messageID"]!.GetValue<string>() == lastStep).Select(x => x["text"]!.GetValue<string>().Trim());
        var narration = texts.First(x => x["messageID"]!.GetValue<string>() != lastStep)["text"]!.GetValue<string>().Trim();

        Assert.Multiple(() =>
        {
            Assert.That(parser.Reply, Is.EqualTo(string.Join("\n\n", answer)));
            Assert.That(parser.Reply, Does.Not.Contain(narration), "the narration is not part of the reply");
            Assert.That(
                events.OfType<CodingAgentEvent.Text>().Select(x => x.Value).Any(x => x.Contains(narration)), Is.True,
                "but it still reached the chat as it was written");
        });
    }

    [Test]
    public async Task It_names_the_files_a_patch_changed_and_the_session_to_resume()
    {
        var (parser, events) = await ReadAsync(4096);

        var patched = OfType("tool_use")
            .Where(x => x["part"]!["tool"]!.GetValue<string>() == "apply_patch")
            .SelectMany(x => x["part"]!["state"]!["input"]!["patchText"]!.GetValue<string>().Split('\n'))
            .Where(line => line.StartsWith("*** Update File: ") || line.StartsWith("*** Add File: "))
            .Select(line => line[(line.IndexOf(": ", StringComparison.Ordinal) + 2)..].Trim().Replace("/workspace/", string.Empty))
            .Distinct()
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(patched, Is.Not.Empty, "the recording edited files with a patch");
            Assert.That(events.OfType<CodingAgentEvent.FileChanged>().Select(x => x.Path).Distinct(), Is.EquivalentTo(patched));
            Assert.That(parser.SessionId, Is.EqualTo(Lines[0]["sessionID"]!.GetValue<string>()));
        });
    }
}
