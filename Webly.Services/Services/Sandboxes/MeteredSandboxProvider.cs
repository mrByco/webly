using Microsoft.Extensions.Options;
using Webly.Data.Models.Usage;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Usage;

namespace Webly.Services.Services.Sandboxes;

/// <summary>
/// Records how long every sandbox ran, whichever provider started it and whoever asked.
///
/// A sandbox bills by the second, and there are two places that start one — the workspace registry for editing
/// and the deployment runner for a publish — with several ways each to stop one: idle, deleted, released at
/// shutdown, failed halfway through starting. Timing each of those paths would be six places to forget one. Every
/// sandbox passes through the provider on its way in and through <see cref="ISandbox.DisposeAsync"/> on its way
/// out, so that is where the clock is: one wrapper, around whichever provider configuration chose.
///
/// A process that is killed records nothing for the sandboxes it had open — their stop never happens here. The
/// providers' first-use sweeps reclaim those machines; the report is short by their time, which is the honest
/// direction to be wrong in.
/// </summary>
public class MeteredSandboxProvider(
    ISandboxProvider inner,
    IUsageRecorder recorder,
    IOptions<SandboxOptions> options) : ISandboxProvider
{
    public string Name => inner.Name;

    public async Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        var sandbox = await inner.StartAsync(spec, cancellationToken);

        return spec.Meter is null ? sandbox : new MeteredSandbox(sandbox, spec.Meter, started, recorder, options.Value.CostPerHour);
    }

    private sealed class MeteredSandbox(
        ISandbox inner,
        SandboxMeter meter,
        DateTime started,
        IUsageRecorder recorder,
        decimal costPerHour) : ISandbox
    {
        private int _disposed;

        public string Id => inner.Id;
        public Uri AgentUrl => inner.AgentUrl;
        public string AgentToken => inner.AgentToken;

        public Task WriteTreeAsync(WorkspaceTree tree, CancellationToken cancellationToken = default) =>
            inner.WriteTreeAsync(tree, cancellationToken);

        public Task<WorkspaceTree> ReadTreeAsync(CancellationToken cancellationToken = default) =>
            inner.ReadTreeAsync(cancellationToken);

        public Task<SandboxCommandResult> RunAsync(
            SandboxCommand command,
            Func<SandboxOutput, Task>? onOutput = null,
            CancellationToken cancellationToken = default) =>
            inner.RunAsync(command, onOutput, cancellationToken);

        public Task StartDevServerAsync(
            string basePath,
            IReadOnlyDictionary<string, string>? environment = null,
            CancellationToken cancellationToken = default) =>
            inner.StartDevServerAsync(basePath, environment, cancellationToken);

        public Task TouchPreviewAsync(CancellationToken cancellationToken = default) =>
            inner.TouchPreviewAsync(cancellationToken);

        public Task<DevServerLog> ReadDevServerLogAsync(long since = 0, CancellationToken cancellationToken = default) =>
            inner.ReadDevServerLogAsync(since, cancellationToken);

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
            inner.IsHealthyAsync(cancellationToken);

        public Task<SandboxHealth> ReadHealthAsync(CancellationToken cancellationToken = default) =>
            inner.ReadHealthAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            // Once: a sandbox disposed twice — a release racing a shutdown — ran once.
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            await inner.DisposeAsync();

            var elapsed = DateTime.UtcNow - started;

            await recorder.RecordAsync(new UsageEntry(
                meter.Kind,
                UsageOutcome.Completed,
                meter.UserId,
                meter.SiteId,
                meter.SiteName,
                (long)elapsed.TotalMilliseconds,
                Math.Round((decimal)elapsed.TotalHours * costPerHour, 6)));
        }
    }
}
