using System.Text.RegularExpressions;

namespace Webly.Services.Agent;

/// <summary>
/// Reads the toolchain's own output for the two questions this product asks it: did that break the site, and
/// what would you tell the person?
///
/// Three callers, two formats. <c>next dev</c> is what a turn checks, so that a compile error reaches the chat
/// while somebody can still say "fix it"; <c>tsc</c> is the second half of that check, because the dev server
/// cannot see a type error at all; and <c>next build</c> is what a publish runs, and its failure is the one that
/// stops a site going live. The build's tail becomes
/// <c>Deployment.ErrorDetail</c>, which the settings screen shows folded away — and it went there raw until a
/// publish was watched in a browser, ANSI escapes, the build machine's absolute paths and SWC's Rust backtrace
/// included.
///
/// Its own class rather than two private methods on <see cref="AgentTurnService"/> because both halves are
/// about somebody else's output format, both were wrong in ways only running them showed, and both are worth a
/// test that does not need a sandbox.
/// </summary>
public static partial class CompilerOutput
{
    /// <summary>
    /// What a broken build looks like in the log, taken from the log rather than from memory.
    ///
    /// <c>⨯ ./</c> is the one that matters and the one that was missing. It is how <c>next dev</c> prefixes a
    /// file it could not compile, and a syntax error — the commonest way an edit breaks a page — produces
    /// exactly that followed by <c>Caused by: Syntax Error</c>, with none of the three phrases this used to
    /// look for. So the compile report was silent for the case it exists for. <c>Module not found</c> is the
    /// dev server's own wording for an import that does not resolve; <c>Failed to compile</c> and
    /// <c>Type error:</c> are <c>next build</c>'s, which is the same log shape on the publish path.
    ///
    /// A type error is deliberately not on this list, because it cannot be: <c>next dev</c> compiles with SWC,
    /// which strips types without checking them, so a file with a type error compiles and serves — verified,
    /// not assumed. <see cref="SaysTheTypesBroke"/> is the other half, reading <c>tsc</c>'s output instead, and
    /// <c>next build</c> at publish time is the backstop for both.
    /// </summary>
    public static readonly string[] Markers =
        ["⨯ ./", "Failed to compile", "Module not found", "Syntax Error", "Type error:"];

    /// <summary>Whether anything in this slice of the log says the site stopped compiling.</summary>
    public static bool SaysTheBuildBroke(string log) =>
        Markers.Any(marker => log.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The compiler's words, as something to put in a chat.
    ///
    /// Three things are dropped and none of them is information. The log is a terminal's, so it carries ANSI
    /// colour codes that a browser renders as a literal <c>[31m</c> in the middle of the error. A syntax error
    /// from SWC arrives with a seventeen-frame Rust stack backtrace of <c>&lt;unknown&gt;</c>, which is the
    /// compiler's own internals and says nothing at all about the person's page. And everything before the
    /// complaint goes: on a cold workspace the slice starts at the dev server's first breath, so the block
    /// opened with npm's banner, the Next.js version and "Ready in 1269ms" — eight lines of nothing above the
    /// one line somebody needs. The error is what this is for, so the error is where it starts.
    /// </summary>
    public static string Readable(string log)
    {
        var lines = AnsiCodes().Replace(log, string.Empty).Split('\n');

        // From the first line that complains. Not from the first line at all, and not a fixed number of lines
        // of context: what counts as context here is startup chatter from before the edit even happened.
        var first = Array.FindIndex(lines, line => Markers.Any(marker =>
            line.Contains(marker, StringComparison.OrdinalIgnoreCase)));

        if (first > 0) lines = lines[first..];
        var kept = new List<string>(lines.Length);
        var inBacktrace = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("Stack backtrace:", StringComparison.Ordinal))
            {
                inBacktrace = true;
                continue;
            }

            // The frames, their indented "at …/threadpool.c:123:5" continuations, and the blank lines among them.
            // Anything starting at column 0 with something in it ends the section — which is how "Import trace for
            // requested module:" survives while sixteen frames of node's own thread pool do not.
            if (inBacktrace && (line.Trim().Length == 0 || StackFrame().IsMatch(line) || char.IsWhiteSpace(line[0])))
                continue;

            inBacktrace = false;

            // A request's own log line is not a compiler complaint. `GET / 500 in 9212ms` is the dev server
            // saying it served the error page, which the person can see for themselves in the pane beside the
            // chat, and it arrives at the end of the block looking like part of the diagnosis.
            if (RequestLine().IsMatch(line)) continue;

            kept.Add(line);
        }

        return string.Join('\n', Deduplicate(kept)).Trim();
    }

    /// <summary>
    /// One copy of each complaint, in the order they first appeared.
    ///
    /// <c>next dev</c> compiles on demand and logs the failure **again for every request**, and the turn's own
    /// check asks for the page and then retries for fifteen seconds while the dev server warms up — so the
    /// slice of log a broken page produces holds the same twelve-line error two or three times over. Watched on
    /// screen: "Your site is not compiling" followed by one mistake written out three times, which reads like
    /// three mistakes and buries the one line naming the file and the row. That is the same failure this whole
    /// class exists to prevent — npm's banner above the error was the first version of it.
    ///
    /// A block starts where the dev server starts one: a line whose first characters are <c>⨯ ./</c> or
    /// <c>x ./</c>, the two forms it prefixes an uncompilable file with. Deliberately not any marker — <c>Syntax
    /// Error</c> appears *inside* a block, under <c>Caused by:</c>, and splitting there would cut every error in
    /// half and then call the halves distinct. Two genuinely different errors differ in their text and both
    /// survive.
    /// </summary>
    private static List<string> Deduplicate(List<string> lines)
    {
        var blocks = new List<List<string>>();

        foreach (var line in lines)
        {
            var starts = line.TrimStart().StartsWith("⨯ ./", StringComparison.Ordinal)
                || line.TrimStart().StartsWith("x ./", StringComparison.Ordinal);

            if (starts || blocks.Count == 0) blocks.Add([]);

            blocks[^1].Add(line);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();

        foreach (var block in blocks)
        {
            // Compared by its body, not by the glyph in front of it. `next dev` writes the first occurrence
            // with `⨯` and every repeat with `x`, so two reports of one mistake are never the same string —
            // which is how the first version of this let a duplicate through and only collapsed the rest.
            var text = string.Join('\n', block.Select(line => line.TrimEnd())).Trim();
            var key = Marker().Replace(text, "./", 1);

            if (text.Length > 0 && !seen.Add(key)) continue;

            kept.AddRange(block);
        }

        return kept;
    }

    /// <summary>
    /// How many error lines reach the chat. A tsc run that is unhappy about a missing module is unhappy about it
    /// once per import, and forty lines of the same complaint pushes the sentence explaining it off the screen.
    /// </summary>
    private const int MaxTypeErrorLines = 20;

    /// <summary>
    /// Whether a failed <c>npm run typecheck</c> failed because of the site rather than because of us.
    ///
    /// The exit code alone cannot answer that, and the difference matters more here than anywhere else in this
    /// file: a missing <c>node_modules</c>, a script that is not there and an <c>npm</c> that could not start all
    /// exit non-zero, and reporting one of those as "your site is not compiling" tells a customer their site is
    /// broken when what is broken is Webly's sandbox. So the claim is made only when <c>tsc</c> names a file and a
    /// diagnostic code, which is the one thing nothing else prints.
    /// </summary>
    public static bool SaysTheTypesBroke(string output) => TypeErrorLine().IsMatch(output);

    /// <summary>
    /// <c>tsc</c>'s complaints, as something to put in a chat.
    ///
    /// The head rather than the tail, which is the opposite of what the dev server's log wants and for the same
    /// reason: a compile error is the last thing <c>next dev</c> says, and a type error is the <i>first</i> thing
    /// <c>tsc</c> says — everything after it is often the same mistake seen from another file. The count is kept
    /// when the rest is dropped, because "and 60 more" is the difference between a typo and a rename that went
    /// wrong.
    ///
    /// npm's two banner lines are dropped as well. The command asks for <c>--silent</c>, which suppresses them,
    /// and they are dropped here anyway: which lines an npm prints about itself is not a thing to depend on, and
    /// a block that opens with <c>&gt; tsc --noEmit</c> is talking about the tool rather than the site.
    /// </summary>
    public static string TypeErrors(string output)
    {
        var lines = AnsiCodes().Replace(output, string.Empty)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !line.StartsWith("> ", StringComparison.Ordinal))
            .SkipWhile(line => line.Trim().Length == 0)
            .ToList();

        if (lines.Count <= MaxTypeErrorLines) return string.Join('\n', lines).Trim();

        var dropped = lines.Count - MaxTypeErrorLines;

        return string.Join('\n', lines[..MaxTypeErrorLines]).Trim()
            + $"\n\n…and {dropped} more line{(dropped == 1 ? string.Empty : "s")}.";
    }

    [GeneratedRegex("\u001b\\[[0-9;]*m")]
    private static partial Regex AnsiCodes();

    /// <summary>The <c>⨯ ./</c> or <c>x ./</c> a complaint opens with, so two of them can be compared by body.</summary>
    [GeneratedRegex(@"^\s*(⨯|x)\s+\./")]
    private static partial Regex Marker();

    /// <summary>The dev server's own access log — <c>GET / 500 in 9212ms</c> — which is not a complaint.</summary>
    [GeneratedRegex(@"^\s*(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\s+\S+\s+\d{3}\s+in\s+\d+ms\s*$")]
    private static partial Regex RequestLine();

    [GeneratedRegex(@"^\s*\d+: ")]
    private static partial Regex StackFrame();

    /// <summary>
    /// <c>src/app/page.tsx(11,7): error TS2322: …</c> — the shape every <c>tsc</c> diagnostic has and nothing else
    /// does. The file and position are not read, only required: it is their presence that says tsc got as far as
    /// checking the site.
    /// </summary>
    [GeneratedRegex(@"\(\d+,\d+\): error TS\d+", RegexOptions.Multiline)]
    private static partial Regex TypeErrorLine();
}
