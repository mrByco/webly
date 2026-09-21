using System.Text.RegularExpressions;

namespace Webly.Services.Agent;

/// <summary>
/// Reads <c>next dev</c>'s output for the one question a turn asks it: did this edit break the site, and what
/// would you tell the person?
///
/// Its own class rather than two private methods on <see cref="AgentTurnService"/> because both halves are
/// about somebody else's output format, both were wrong in ways only running them showed, and both are worth
/// a test that does not need a sandbox.
/// </summary>
/// <remarks>
/// Named for what it does rather than for what it reads, because <c>DevServerLog</c> is already the record the
/// sandbox hands back — the text and how far through it that reached.
/// </remarks>
public static partial class DevServerLogReader
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
    /// not assumed. That is what <c>npm run typecheck</c> is for (<c>AGENTS.md</c> asks the agent to run it)
    /// and <c>next build</c> at publish time is the backstop.
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

            // The frames themselves, and the blank lines among them. Anything else ends the section.
            if (inBacktrace && (line.Trim().Length == 0 || StackFrame().IsMatch(line))) continue;

            inBacktrace = false;
            kept.Add(line);
        }

        return string.Join('\n', kept).Trim();
    }

    [GeneratedRegex("\u001b\\[[0-9;]*m")]
    private static partial Regex AnsiCodes();

    [GeneratedRegex(@"^\s*\d+: ")]
    private static partial Regex StackFrame();
}
