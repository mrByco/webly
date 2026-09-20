using Webly.Services.Services.Repositories;

namespace Webly.Services.Services.Sandboxes;

/// <summary>One line of a command's output, as it happens.</summary>
public record SandboxOutput(bool IsError, string Text);

/// <summary>A command to run in the workspace.</summary>
public record SandboxCommand(
    string Command,
    IReadOnlyList<string> Arguments,
    TimeSpan? Timeout = null,
    IReadOnlyDictionary<string, string>? Environment = null);

public record SandboxCommandResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// A slice of the dev server's output, and how far through it that slice reached.
/// </summary>
/// <param name="Text">What the dev server said — all of it, or only what followed the requested offset.</param>
/// <param name="Offset">
/// Total output so far. Pass it back as <c>since</c> next time to get only what happened in between. It counts
/// everything the dev server has ever written, not what is still buffered, so it keeps meaning the same thing
/// after the buffer has rolled over.
/// </param>
public record DevServerLog(string Text, long Offset)
{
    public static DevServerLog Empty { get; } = new(string.Empty, 0);
}

/// <summary>What a sandbox is asked for when it starts.</summary>
/// <param name="SiteNanoid">Only for naming and logs; a sandbox never learns anything else about the site.</param>
/// <param name="Environment">Injected into every command — where an agent's provider key lives for the run.</param>
public record SandboxSpec(string SiteNanoid, IReadOnlyDictionary<string, string> Environment);

public class SandboxException(string message, string? detail = null) : Exception(message)
{
    public string? Detail { get; } = detail;
}

/// <summary>
/// A running, isolated machine with the workspace on it: a filesystem, node, a network, and nothing of
/// ours. Everything Webly does to a site's source happens through one of these.
///
/// <b>The isolation is the permission model.</b> A coding agent can write any file in the workspace and
/// run any command — that is the point of it — so what keeps it safe is not a tool list but the fact that
/// it is somewhere else: no database, no Vercel token, no git history, no other customer's tree, and an
/// egress policy it cannot change. That is a real change from a document-editing agent, and it is why the
/// sandbox interface is this small: every capability it does not have is a capability that cannot be
/// granted by accident.
///
/// Providers differ in how a machine is started and reached, and in nothing else — see
/// <see cref="ISandboxProvider"/> and <c>tools/sandbox-agent</c>.
/// </summary>
public interface ISandbox : IAsyncDisposable
{
    /// <summary>The provider's id, for logs and for stopping it.</summary>
    string Id { get; }

    /// <summary>
    /// Where this sandbox's agent answers, and the token for it. The preview proxy needs both: it forwards
    /// a browser's request to <c>{AgentUrl}/preview/…</c> with the token attached, which is how a customer
    /// sees their own dev server without the sandbox being reachable from the internet.
    /// </summary>
    Uri AgentUrl { get; }

    string AgentToken { get; }

    /// <summary>Replaces the workspace with this tree. How a commit becomes something to edit.</summary>
    Task WriteTreeAsync(WorkspaceTree tree, CancellationToken cancellationToken = default);

    /// <summary>
    /// The workspace as it stands, minus build output and dependencies (the ignore list lives in the
    /// sandbox agent, because that is where it can be applied before the bytes travel). How an edit
    /// becomes a commit.
    /// </summary>
    Task<WorkspaceTree> ReadTreeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a command, streaming its output. <paramref name="onOutput"/> is how an agent's stdout reaches
    /// the person watching, one line at a time, rather than after it finishes.
    /// </summary>
    Task<SandboxCommandResult> RunAsync(
        SandboxCommand command,
        Func<SandboxOutput, Task>? onOutput = null,
        CancellationToken cancellationToken = default);

    /// <summary>Starts the Next.js dev server, once. Idempotent.</summary>
    Task StartDevServerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the dev server for the home page, and waits for it.
    /// </summary>
    /// <remarks>
    /// This exists because <c>next dev</c> compiles <b>on demand</b>. Nothing is compiled until something asks
    /// for a page, so straight after a turn the dev server has no opinion at all about the files the agent just
    /// wrote — and a compile-error check at that moment finds an empty log and reports success. The person then
    /// hears nothing, and discovers the breakage when they press Publish.
    ///
    /// One request is enough to make the question answerable. The response is not interesting; whether it
    /// compiled is, and that is in the log.
    /// </remarks>
    Task TouchPreviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The dev server's output, optionally only what arrived after <paramref name="since"/>. This is where a
    /// compile error lives, and a compile error is both the most useful thing to show the person and the thing
    /// the next agent turn has to be told about.
    ///
    /// <b>The offset is not optional in practice.</b> The log is cumulative for the life of the dev server, so a
    /// caller that reads all of it after a turn sees every earlier turn's output too — and a compile error from
    /// three turns ago then reads as a compile error now, permanently, however many times the person fixes it.
    /// Record <see cref="DevServerLog.Offset"/> before doing something, pass it back afterwards, and what comes
    /// back is what that something caused.
    /// </summary>
    Task<DevServerLog> ReadDevServerLogAsync(long since = 0, CancellationToken cancellationToken = default);

    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Where sandboxes come from. Implementations do two things — start a machine from Webly's sandbox image
/// and say where its agent answers — because everything else is the one HTTP contract in
/// <c>tools/sandbox-agent</c>.
///
/// That split is deliberate. Each provider's own API for uploading files, running processes and exposing
/// ports is large, different and unverifiable from here; reducing them to "start this image, give me a
/// URL" means a second provider is fifty lines, and the file and process mechanics have one
/// implementation that is tested once.
/// </summary>
public interface ISandboxProvider
{
    /// <summary>For logs and for the configuration that selects it.</summary>
    string Name { get; }

    Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken = default);
}
