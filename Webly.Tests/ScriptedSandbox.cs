using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sandboxes;

namespace Webly.Tests;

/// <summary>
/// A sandbox whose one command prints what it is given and exits as it is told — and remembers the command, which
/// is how a test asks what an agent would have run.
/// </summary>
internal sealed class ScriptedSandbox(int exitCode, params string[] stdout) : ISandbox
{
    public string Id => "scripted";
    public Uri AgentUrl => new("http://127.0.0.1/");
    public string AgentToken => string.Empty;

    public SandboxCommand? Command { get; private set; }

    public async Task<SandboxCommandResult> RunAsync(
        SandboxCommand command,
        Func<SandboxOutput, Task>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        Command = command;

        foreach (var line in stdout)
            if (onOutput is not null) await onOutput(new SandboxOutput(IsError: false, line));

        return new SandboxCommandResult(exitCode, string.Concat(stdout));
    }

    public Task WriteTreeAsync(WorkspaceTree tree, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceTree> ReadTreeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<WorkspaceTree?> ReadBuildOutputAsync(string directory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task StartDevServerAsync(string basePath, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task TouchPreviewAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DevServerLog> ReadDevServerLogAsync(long since = 0, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<SandboxHealth> ReadHealthAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
