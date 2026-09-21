using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Agent.Agents;

/// <summary>
/// An agent that edits the site without a model behind it, for development and tests.
///
/// It exists because everything around a turn is worth exercising on its own: the workspace starting, the
/// dev server compiling, the tree coming back, the commit, the version row, the preview refreshing, the
/// history entry, the publish. All of that is Webly's code, none of it is the model's, and without this
/// class none of it can be run at all without a provider key — which makes the most expensive credential in
/// the system a prerequisite for testing the parts that do not use it.
///
/// <b>It makes a real edit, on purpose.</b> A stub that returned prose and touched nothing would leave the
/// interesting half untested: the tree would be identical, so no version would be committed, and the path
/// from "the agent wrote a file" to "the customer sees it" would never run. So it rewrites the home page's
/// headline with the person's own words, through the same sandbox contract the real agents use. That also
/// makes its failure mode honest — if the template's home page changes shape, this notices.
///
/// Registered like any other agent and selected by key (<c>mock</c>), so nothing above it knows the
/// difference — which is the point of <see cref="ICodingAgent"/> existing. It reports itself configured only
/// when <c>Agent:Mock:Enabled</c> is set, so it cannot become the fallback in a deployment by accident.
/// </summary>
public class MockCodingAgent(
    IOptions<CodingAgentOptions> options,
    ILogger<MockCodingAgent> logger) : ICodingAgent
{
    public const string AgentKey = "mock";

    public string Key => AgentKey;

    private readonly CodingAgentOptions _options = options.Value;

    public bool IsConfigured => _options.Mock.Enabled;

    /// <summary>
    /// The file it edits and the shape it looks for: the home page, and the <c>headline</c> it passes to the
    /// hero.
    ///
    /// The page and not the component, which is where an earlier version of this looked. The component renders
    /// <c>{headline}</c> from a prop, so replacing that with a literal would leave the prop unused and the
    /// template is <c>strict</c> — a mock agent that produced a file the build rejects would fail for its own
    /// reasons and teach nothing. Editing the copy at the call site is also what a real edit looks like.
    ///
    /// Deliberately not a search across the tree: a mock that looked for something to change would sometimes
    /// change something surprising, and then a failing test would be about this class.
    /// </summary>
    private const string TargetPath = "src/app/page.tsx";

    private const string TargetAttribute = "headline=\"";

    /// <summary>
    /// What a person types to make their site stop compiling, and the line the mock leaves behind when they do.
    ///
    /// The build-failure path is a real part of the product — the dev server's log is scanned after every turn and
    /// its own words go into the chat, because the person's next message is what fixes it — and until this existed
    /// there was no way to see it happen without a model key and a deliberately unhelpful prompt. So the mock
    /// takes the instruction literally.
    ///
    /// An unterminated expression, because that is a <i>syntax</i> error, which is what <c>next dev</c> complains
    /// about out loud. A broken import that nothing uses would not do: it is elided as possibly-a-type before
    /// anything resolves it.
    /// </summary>
    public const string BreakPhrase = "break the build";

    /// <summary>
    /// The other half of the same idea, for the check the dev server cannot make.
    ///
    /// <c>next dev</c> compiles with SWC, which strips types rather than checking them, so this snippet serves a
    /// page perfectly and appears in no log — verified against a real dev server, which answered 200 — which is
    /// exactly why a turn runs <c>npm run typecheck</c> as well, and why that path needs its own way to be driven
    /// without a model key.
    ///
    /// Not <c>export</c>ed, and that is the whole difference between one error and two. Next.js generates a type in
    /// <c>.next/types</c> that constrains a page's exports to the ones it knows, so an extra exported const also
    /// produces a complaint about a generated file with a four-hundred-character type name in it — true, and useless
    /// to read. A file-scope const is the mistake without the noise.
    /// </summary>
    public const string BreakTypesPhrase = "break the types";

    private const string BreakMarker = "// Added by the mock agent on purpose. Ask for anything else and it goes.";

    private const string BreakSnippet = $"\n\n{BreakMarker}\nexport const broken = (\n";

    private const string BreakTypesSnippet = $"\n\n{BreakMarker}\nconst mistyped: string = 42;\n";

    public async Task<CodingAgentOutcome> RunAsync(
        ISandbox sandbox,
        CodingAgentRequest request,
        Func<CodingAgentEvent, Task> onEvent,
        CancellationToken cancellationToken = default)
    {
        await onEvent(new CodingAgentEvent.Activity("Reading your site", TargetPath));

        var tree = await sandbox.ReadTreeAsync(cancellationToken);
        var file = tree.Files.FirstOrDefault(x => x.Path == TargetPath)
            ?? throw new SandboxException(
                "The mock agent could not find the page to edit.",
                $"{TargetPath} is not in this site. The mock agent only understands the starter template.");

        // Repaired first, always: a turn starts from a working file whatever the last one did, so asking for
        // anything at all after a break fixes it. That is the product's own story about build errors — the next
        // message is the fix — and it means the mock cannot leave a site permanently broken.
        var before = Repaired(Encoding.UTF8.GetString(file.Content));

        var breaking = request.Message.Contains(BreakPhrase, StringComparison.OrdinalIgnoreCase);
        var mistyping = request.Message.Contains(BreakTypesPhrase, StringComparison.OrdinalIgnoreCase);
        var headline = Headline(request.Message);

        var after = breaking ? before + BreakSnippet
            : mistyping ? before + BreakTypesSnippet
            : ReplaceFirstHeadline(before, headline);

        if (after is null)
            throw new SandboxException(
                "The mock agent could not find a headline to change.",
                $"No {TargetAttribute}…\" in {TargetPath}. The mock agent only understands the starter template.");

        // Written through the sandbox's own contract rather than onto a path, so this exercises the same
        // write path a real agent's CLI does.
        await WriteAsync(sandbox, tree, after, cancellationToken);

        await onEvent(new CodingAgentEvent.FileChanged(TargetPath));
        await onEvent(new CodingAgentEvent.Activity("Editing a file", TargetPath));

        var reply = breaking
            ? "I left an unfinished expression in the home page, so it will not compile. Ask me for anything else and I will take it out."
            : mistyping
                ? "I put a number where the home page wants a string. The page still renders, so only the typecheck will notice. Ask me for anything else and I will take it out."
                : $"I put \"{headline}\" at the top of the home page.";

        await onEvent(new CodingAgentEvent.Text(reply));

        logger.LogInformation(
            breaking || mistyping
                ? "The mock agent broke {Path} on purpose."
                : "The mock agent set the headline to {Headline}.",
            breaking || mistyping ? TargetPath : headline);

        return new CodingAgentOutcome(
            reply,
            Summary: breaking || mistyping
                ? "Broke the home page on purpose"
                : $"Set the headline to \"{Shorten(headline, 40)}\"",
            Details: "Written by the mock agent, which has no model behind it.",
            // A session id, so the workspace's resume path is exercised too — it means nothing to this agent.
            SessionId: "mock-session");
    }

    /// <summary>
    /// Writes the changed file back by replacing the whole tree. Wasteful, and the right shape: it is the
    /// same <c>WriteTreeAsync</c> the workspace seeds with, so nothing here is a code path only the mock uses.
    /// </summary>
    private static Task WriteAsync(
        ISandbox sandbox,
        WorkspaceTree tree,
        string contents,
        CancellationToken cancellationToken)
    {
        var files = tree.Files
            .Select(x => x.Path == TargetPath
                ? new WorkspaceFile(x.Path, Encoding.UTF8.GetBytes(contents))
                : x)
            .ToList();

        return sandbox.WriteTreeAsync(new WorkspaceTree(files), cancellationToken);
    }

    /// <summary>
    /// The file without the mock's own deliberate breakage, or unchanged if there is none. Keyed on the marker it
    /// writes rather than on the broken expression, so it can never remove something a real agent wrote.
    /// </summary>
    private static string Repaired(string source)
    {
        var marker = source.IndexOf(BreakMarker, StringComparison.Ordinal);

        return marker < 0 ? source : source[..marker].TrimEnd() + "\n";
    }

    /// <summary>The person's message, as something that fits in a heading.</summary>
    private static string Headline(string message)
    {
        var cleaned = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        var sentence = cleaned.Split(['.', '!', '?'], 2)[0].Trim();

        return Shorten(sentence.Length > 0 ? sentence : cleaned, 60);
    }

    /// <summary>
    /// Replaces the first <c>headline="…"</c> attribute's value.
    ///
    /// String work rather than a parser because the input is JSX, which no HTML parser reads correctly, and
    /// because this is a development aid. It refuses rather than guesses when the shape is not there.
    /// </summary>
    private static string? ReplaceFirstHeadline(string source, string headline)
    {
        var start = source.IndexOf(TargetAttribute, StringComparison.Ordinal);
        if (start < 0) return null;

        var valueStart = start + TargetAttribute.Length;
        var valueEnd = source.IndexOf('"', valueStart);
        if (valueEnd < 0) return null;

        // A quote would close the attribute and a brace would make it a JSX expression, so both are dropped
        // rather than escaped: a mock that could write a file which does not compile is worse than no mock.
        var safe = headline
            .Replace("\"", string.Empty)
            .Replace("{", string.Empty)
            .Replace("}", string.Empty)
            .Replace("<", string.Empty)
            .Replace(">", string.Empty);

        return string.Concat(source.AsSpan(0, valueStart), safe, source.AsSpan(valueEnd));
    }

    private static string Shorten(string value, int length) =>
        value.Length <= length ? value : value[..length].TrimEnd();
}
