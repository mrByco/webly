using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Models.Sites;
using Webly.Data.Models.Usage;
using Webly.Data.Repositories.Usage;
using Webly.Services.Agent;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sandboxes;
using Webly.Services.Services.Usage;

namespace Webly.Tests;

/// <summary>
/// What things cost: the recorder writing a row, the sandbox clock, and the report adding them up — against a
/// real Postgres, because the report is aggregates the database works out and a fake would only test the fake.
/// </summary>
public class UsageTests : PostgresTestBase
{
    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddDbContext<WeblyDbContext>(options => options.UseNpgsql(ConnectionString));
        services.AddScoped<IUsageRepository, UsageRepository>();
        services.AddSingleton<IUsageRecorder>(x => new UsageRecorder(
            x.GetRequiredService<IServiceScopeFactory>(), NullLogger<UsageRecorder>.Instance));

        return services.BuildServiceProvider();
    }

    private async Task<(User User, Site Site)> SiteAsync(string email, string name)
    {
        await using var db = CreateContext();

        var user = new User { Email = email, DisplayName = name };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var site = new Site { OwnerId = user.Id, Name = name, Slug = name.ToLowerInvariant().Replace(' ', '-') };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        return (user, site);
    }

    [Test]
    public async Task A_turn_is_recorded_with_what_the_agent_reported()
    {
        var (user, site) = await SiteAsync("a@example.com", "Bakery");
        await using var services = Services();

        await services.GetRequiredService<IUsageRecorder>().RecordAsync(new UsageEntry(
            UsageKind.AgentTurn, UsageOutcome.Failed, user.Id, site.Id, site.Name, 41_000, 0.34m, "opencode",
            new AgentUsage("openai/gpt-5.5", 4922, 250, 5632, 0, 0.34m), new string('x', 500)));

        await using var db = CreateContext();
        var row = await db.UsageRecords.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(row.Nanoid, Is.Not.Empty);
            Assert.That(row.CreatedAt, Is.Not.EqualTo(default(DateTime)));
            Assert.That(row.Outcome, Is.EqualTo(UsageOutcome.Failed), "a turn that failed still cost what it cost");
            Assert.That(row.CostUsd, Is.EqualTo(0.34m));
            Assert.That((row.Agent, row.Model), Is.EqualTo(("opencode", "openai/gpt-5.5")));
            Assert.That((row.InputTokens, row.OutputTokens, row.CacheReadTokens), Is.EqualTo((4922L, 250L, 5632L)));
            Assert.That(row.Description!.Length, Is.EqualTo(200), "the message is a reminder, not a copy");
        });
    }

    [Test]
    public async Task Recording_never_throws_at_the_work_it_describes()
    {
        await using var services = Services();

        // A user that does not exist: the foreign key refuses the row, and the recorder says so in the log only.
        Assert.That(
            async () => await services.GetRequiredService<IUsageRecorder>().RecordAsync(new UsageEntry(
                UsageKind.AgentTurn, UsageOutcome.Completed, 999_999, null, null, 1, 1m)),
            Throws.Nothing);
    }

    [Test]
    public async Task A_sandbox_is_recorded_once_when_it_stops_at_the_hourly_rate()
    {
        var (user, site) = await SiteAsync("b@example.com", "Forge");
        await using var services = Services();

        var provider = new MeteredSandboxProvider(
            new NothingProvider(),
            services.GetRequiredService<IUsageRecorder>(),
            Options.Create(new SandboxOptions { CostPerHour = 3600m }));

        var sandbox = await provider.StartAsync(new SandboxSpec(site.Nanoid, new Dictionary<string, string>())
        {
            Meter = new SandboxMeter(UsageKind.EditingSandbox, user.Id, site.Id, site.Name)
        });

        await Task.Delay(50);
        await sandbox.DisposeAsync();
        await sandbox.DisposeAsync();

        await using var db = CreateContext();
        var row = await db.UsageRecords.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(row.Kind, Is.EqualTo(UsageKind.EditingSandbox));
            Assert.That(row.DurationMs, Is.GreaterThanOrEqualTo(50));
            Assert.That(row.CostUsd, Is.EqualTo(Math.Round(row.DurationMs / 1000m, 2)).Within(0.02m),
                "a dollar a second at 3600 an hour");
            Assert.That(row.SiteId, Is.EqualTo(site.Id));
        });
    }

    [Test]
    public async Task Deleting_a_site_keeps_what_it_cost()
    {
        var (user, site) = await SiteAsync("c@example.com", "Old Shop");
        await using var services = Services();

        await services.GetRequiredService<IUsageRecorder>().RecordAsync(new UsageEntry(
            UsageKind.AgentTurn, UsageOutcome.Completed, user.Id, site.Id, site.Name, 1000, 0.5m, "opencode"));

        await using (var db = CreateContext())
            await db.Sites.Where(x => x.Id == site.Id).ExecuteDeleteAsync();

        var report = services.GetRequiredService<IUsageRepository>();
        var bySite = await report.BySiteAsync(DateTime.UtcNow.AddDays(-1), 10);

        Assert.Multiple(() =>
        {
            Assert.That(bySite, Has.Count.EqualTo(1));
            Assert.That(bySite[0].SiteNanoid, Is.Null, "the site is gone");
            Assert.That(bySite[0].SiteName, Is.EqualTo("Old Shop"), "and the report still says which one it was");
            Assert.That(bySite[0].ModelCostUsd, Is.EqualTo(0.5m));
        });
    }

    [Test]
    public async Task The_report_adds_up_model_and_machine_by_site_user_and_day()
    {
        var (anna, bakery) = await SiteAsync("anna@example.com", "Bakery");
        var (ben, forge) = await SiteAsync("ben@example.com", "Forge");
        await using var services = Services();
        var recorder = services.GetRequiredService<IUsageRecorder>();

        await recorder.RecordAsync(new UsageEntry(UsageKind.AgentTurn, UsageOutcome.Completed, anna.Id, bakery.Id, bakery.Name, 30_000, 0.40m, "opencode",
            new AgentUsage("openai/gpt-5.5", 1000, 200, 5000, 0, 0.40m), "Make the headline bigger"));
        await recorder.RecordAsync(new UsageEntry(UsageKind.AgentTurn, UsageOutcome.Stopped, anna.Id, bakery.Id, bakery.Name, 9_000, 0.10m, "opencode",
            new AgentUsage("openai/gpt-5.5", 300, 50, 0, 0, 0.10m), "Add a menu page"));
        await recorder.RecordAsync(new UsageEntry(UsageKind.EditingSandbox, UsageOutcome.Completed, anna.Id, bakery.Id, bakery.Name, 600_000, 0.05m));
        await recorder.RecordAsync(new UsageEntry(UsageKind.AgentTurn, UsageOutcome.Completed, ben.Id, forge.Id, forge.Name, 20_000, 1.25m, "claude-code",
            new AgentUsage("claude-opus-5", 10, 900, 40000, 7000, 1.25m), "Build me a site"));
        await recorder.RecordAsync(new UsageEntry(UsageKind.PublishSandbox, UsageOutcome.Completed, ben.Id, forge.Id, forge.Name, 120_000, 0.01m));

        var report = services.GetRequiredService<IUsageRepository>();
        var since = DateTime.UtcNow.AddDays(-30);

        var totals = await report.TotalsAsync(since);
        var bySite = await report.BySiteAsync(since, 10);
        var byUser = await report.ByUserAsync(since, 10);
        var byDay = await report.ByDayAsync(since);
        var recent = await report.RecentTurnsAsync(since, 10);

        Assert.Multiple(() =>
        {
            Assert.That(totals.ModelCostUsd, Is.EqualTo(1.75m));
            Assert.That(totals.SandboxCostUsd, Is.EqualTo(0.06m));
            Assert.That(totals.Turns, Is.EqualTo(3));
            Assert.That(totals.UnfinishedTurns, Is.EqualTo(1), "the stopped one");
            Assert.That(totals.SandboxMs, Is.EqualTo(720_000));
            Assert.That(totals.CacheReadTokens, Is.EqualTo(45_000));

            Assert.That(bySite.Select(x => x.SiteName), Is.EqualTo(new[] { "Forge", "Bakery" }), "most expensive first");
            Assert.That(bySite[1].ModelCostUsd, Is.EqualTo(0.50m));
            Assert.That(bySite[1].SandboxMs, Is.EqualTo(600_000));
            Assert.That(bySite[0].OwnerEmail, Is.EqualTo("ben@example.com"));

            Assert.That(byUser.Select(x => x.Email), Is.EqualTo(new[] { "ben@example.com", "anna@example.com" }));
            Assert.That(byUser[1].Turns, Is.EqualTo(2));

            Assert.That(byDay, Has.Count.EqualTo(1));
            Assert.That(byDay[0].ModelCostUsd + byDay[0].SandboxCostUsd, Is.EqualTo(1.81m));

            Assert.That(recent.Select(x => x.Description), Is.EqualTo(new[] { "Build me a site", "Add a menu page", "Make the headline bigger" }),
                "turns only, newest first");
            Assert.That(recent[1].Outcome, Is.EqualTo(UsageOutcome.Stopped));
        });
    }

    /// <summary>A provider whose sandboxes do nothing, so the only thing under test is the clock around them.</summary>
    private sealed class NothingProvider : ISandboxProvider
    {
        public string Name => "nothing";

        public Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken = default) =>
            Task.FromResult<ISandbox>(new NothingSandbox());
    }

    private sealed class NothingSandbox : ISandbox
    {
        public string Id => "nothing";
        public Uri AgentUrl => new("http://127.0.0.1/");
        public string AgentToken => string.Empty;
        public Task WriteTreeAsync(WorkspaceTree tree, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<WorkspaceTree> ReadTreeAsync(CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceTree([]));
        public Task<WorkspaceTree?> ReadBuildOutputAsync(string directory, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceTree?>(null);
        public Task<SandboxCommandResult> RunAsync(SandboxCommand command, Func<SandboxOutput, Task>? onOutput = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SandboxCommandResult(0, string.Empty));
        public Task StartDevServerAsync(string basePath, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TouchPreviewAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DevServerLog> ReadDevServerLogAsync(long since = 0, CancellationToken cancellationToken = default) => Task.FromResult(new DevServerLog(string.Empty, 0));
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<SandboxHealth> ReadHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SandboxHealth(true, true));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
