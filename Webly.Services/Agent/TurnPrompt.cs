using System.Text;

namespace Webly.Services.Agent;

/// <summary>
/// What an agent is asked on a turn, and the one line it is asked to end with — which Webly splits off the reply and
/// commits with.
///
/// One place, because it was two. Each agent built its own prompt and parsed its own summary, line for line alike
/// except where the wording had drifted, so a rule added to one reached half the turns. A convention in the prompt
/// rather than a structured output, because it has to work for every agent whatever its interface — and a missing
/// line is recoverable: the person's own request is a perfectly good commit subject.
///
/// The short prompt is deliberate. The standing instructions live in the site's repository (<c>AGENTS.md</c>, which
/// both CLIs read), and repeating them here would be a second copy to keep in step.
/// </summary>
internal static class TurnPrompt
{
    /// <summary>
    /// The summary is the entry in the owner's history, and a first real turn wrote "Ridgeway Cycles home page was
    /// updated." for a change that added an address, opening hours and a phone number — true, and no help to
    /// somebody scanning a list for the version before something went wrong. Hence the example, which is the part
    /// a model copies.
    /// </summary>
    private const string SummaryInstruction = """
        When you are done, end your reply with a line of the form:

        SUMMARY: <one sentence, under 70 characters>

        It becomes the entry in the site owner's history, so write it for them: in the past tense, naming what
        changed on their site rather than what you did — "Added opening hours and a phone link to the home page",
        not "Updated the home page". If you changed nothing, say SUMMARY: none.
        """;

    private const string Marker = "SUMMARY:";

    /// <summary>A commit subject's length. Git's own convention is 50; 70 is where a history list wraps.</summary>
    private const int MaxSummaryLength = 70;

    public static string Build(CodingAgentRequest request)
    {
        var prompt = new StringBuilder();

        if (request.History.Count > 0)
        {
            prompt.AppendLine("Earlier in this conversation:");

            foreach (var turn in request.History) prompt.AppendLine($"- {turn}");

            prompt.AppendLine();
        }

        prompt.AppendLine(request.Message);
        prompt.AppendLine();
        prompt.AppendLine(SummaryInstruction);

        return prompt.ToString();
    }

    /// <summary>
    /// Splits a reply into what the person reads and the line Webly commits with: the last <c>SUMMARY:</c> line, and
    /// everything after it, comes off the reply; <paramref name="fallbackSummary"/> stands in when there is none or
    /// it says <c>none</c>.
    /// </summary>
    public static (string Reply, string Summary) Split(string text, string fallbackSummary)
    {
        var reply = text.Trim();
        var summary = Shorten(fallbackSummary);
        var lines = reply.Split('\n');

        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();

            if (!line.StartsWith(Marker, StringComparison.OrdinalIgnoreCase)) continue;

            var value = line[Marker.Length..].Trim();

            if (value.Length > 0 && !value.Equals("none", StringComparison.OrdinalIgnoreCase))
                summary = Shorten(value);

            reply = string.Join('\n', lines.Take(index)).TrimEnd();
            break;
        }

        return (reply, summary);
    }

    private static string Shorten(string value) =>
        value.Length <= MaxSummaryLength ? value : $"{value[..(MaxSummaryLength - 3)]}...";
}
