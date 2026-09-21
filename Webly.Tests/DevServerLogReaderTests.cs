using Webly.Services.Agent;

namespace Webly.Tests;

/// <summary>
/// The compile check, against what <c>next dev</c> actually prints.
///
/// The samples below are copied from a real dev server's output, not written from memory, and that is the
/// point of the test: the first version of this check looked for "Failed to compile", "Module not found" and
/// "Type error:", and a syntax error — the commonest way an edit breaks a page — produces none of the three.
/// The editor said nothing at all while the site was down.
/// </summary>
public class DevServerLogReaderTests
{
    /// <summary>A syntax error, ANSI codes and Rust backtrace included, exactly as it arrives.</summary>
    private const string SyntaxError =
        " GET / 200 in 5489ms\n"
        + " ⨯ ./src/app/page.tsx\n"
        + "Error:   \u001b[31mx\u001b[0m Unexpected eof\n"
        + "    ,-[\u001b[36;1;4m/workspace/src/app/page.tsx\u001b[0m:39:25]\n"
        + " \u001b[2m39\u001b[0m | export const broken = (\n"
        + "    `----\n"
        + "\n"
        + "Caused by:\n"
        + "    Syntax Error\n"
        + "\n"
        + "Stack backtrace:\n"
        + "   0: <unknown>\n"
        + "   1: <unknown>\n"
        + "  14: worker\n"
        + "  15: start_thread\n"
        + "Import trace for requested module:\n"
        + "./src/app/page.tsx\n";

    private const string MissingModule =
        " ⨯ ./src/app/page.tsx:1:1\n"
        + "Module not found: Can't resolve './does-not-exist'\n"
        + "> 1 | import { nothing } from './does-not-exist';\n";

    /// <summary>
    /// A type error, which compiles and serves. SWC strips types rather than checking them, so this is not a
    /// gap in the marker list — it is a gap in what the dev server can possibly know, and the reason
    /// <c>AGENTS.md</c> asks the agent to run <c>npm run typecheck</c> itself.
    /// </summary>
    private const string TypeError =
        " ✓ Compiled in 187ms (944 modules)\n GET / 200 in 193ms\n";

    [Test]
    public void A_syntax_error_is_reported()
    {
        Assert.That(DevServerLogReader.SaysTheBuildBroke(SyntaxError), Is.True);
    }

    [Test]
    public void An_import_that_does_not_resolve_is_reported()
    {
        Assert.That(DevServerLogReader.SaysTheBuildBroke(MissingModule), Is.True);
    }

    [Test]
    public void A_healthy_log_is_not_an_error_however_much_it_contains()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DevServerLogReader.SaysTheBuildBroke(TypeError), Is.False);
            Assert.That(DevServerLogReader.SaysTheBuildBroke(" ✓ Ready in 1158ms\n GET / 200 in 42ms\n"), Is.False);
            Assert.That(DevServerLogReader.SaysTheBuildBroke(string.Empty), Is.False);
        });
    }

    [Test]
    public void What_the_person_reads_is_the_error_and_not_the_compiler_is_internals()
    {
        var readable = DevServerLogReader.Readable(SyntaxError);

        Assert.Multiple(() =>
        {
            Assert.That(readable, Does.Contain("Unexpected eof"), "the sentence that says what is wrong");
            Assert.That(readable, Does.Contain("export const broken = ("), "and the line it is on");

            // The two things that are noise. A browser renders the escape sequences literally, and the
            // backtrace is SWC's own frames — seventeen of them, nearly all <unknown>.
            //
            // `string.Contains(char)` rather than `Does.Not.Contain("\u001b[")`, and the difference is not
            // style: NUnit's substring constraint compares with the current culture, and culture-sensitive
            // comparison treats control characters as ignorable — so it finds "ESC [" inside a string whose
            // only "[" is the one in `,-[`. The obvious spelling of this assertion fails on text that is
            // already clean.
            Assert.That(readable.Contains('\u001b'), Is.False, "no ANSI escapes");
            Assert.That(readable, Does.Not.Contain("Stack backtrace"));
            Assert.That(readable, Does.Not.Contain("<unknown>"));
            Assert.That(readable, Does.Not.Contain("start_thread"));

            // The text after the backtrace is kept: it names the file that could not be imported.
            Assert.That(readable, Does.Contain("Import trace for requested module"));
        });
    }
}
