using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Models.Deployments;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Deployments;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Email;
using Webly.Services.Services.Realtime;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sandboxes;
using Webly.Services.UseCases.Sites;

namespace Webly.Tests;

/// <summary>
/// A publish the process died under. The runner only ever picks up <c>Queued</c>, so one cut off in
/// <c>Preparing</c> or <c>Building</c> stayed live for good — and a site has one live publish at a time, so that site
/// could never publish again. Found by restarting the backend in the middle of a build.
/// </summary>
public class InterruptedPublishTests : PostgresTestBase
{
    [Test]
    public async Task What_was_building_fails_and_says_why_while_what_was_waiting_or_finished_is_left_alone()
    {
        var preparing = await DeploymentAsync(DeploymentStatus.Preparing);
        var building = await DeploymentAsync(DeploymentStatus.Building);
        var queued = await DeploymentAsync(DeploymentStatus.Queued);
        var ready = await DeploymentAsync(DeploymentStatus.Ready);

        var mail = new FakeEmailSender();

        await using var services = new ServiceCollection()
            .AddScoped(_ => CreateContext())
            .AddScoped<IDeploymentRepository, DeploymentRepository>()
            .AddScoped<IUserRepository, UserRepository>()
            .AddSingleton<IEmailSender>(mail)
            .BuildServiceProvider();

        await new DeploymentJobRunner(
                services.GetRequiredService<IServiceScopeFactory>(),
                new RunRegistry(),
                NullLogger<DeploymentJobRunner>.Instance)
            .FailInterruptedAsync(CancellationToken.None);

        await using var db = CreateContext();
        var rows = await db.Deployments.ToDictionaryAsync(x => x.Id);

        Assert.Multiple(() =>
        {
            foreach (var cutOff in new[] { preparing, building })
            {
                Assert.That(rows[cutOff].Status, Is.EqualTo(DeploymentStatus.Failed));
                Assert.That(rows[cutOff].Error, Is.EqualTo(DeploymentJobRunner.InterruptedError));
                Assert.That(rows[cutOff].FinishedAt, Is.Not.Null);
            }

            Assert.That(rows[queued].Status, Is.EqualTo(DeploymentStatus.Queued), "the poll will still run it");
            Assert.That(rows[ready].Status, Is.EqualTo(DeploymentStatus.Ready));
            Assert.That(mail.Sent, Has.Count.EqualTo(2), "the owner hears about each, having perhaps closed the tab");
        });
    }

    /// <summary>
    /// A shutdown in the middle of a publish — which is what a deploy of Webly is — is the same event as the one above,
    /// seen before the process went rather than after, and says the same thing. It used to fall through to "Publishing
    /// failed unexpectedly", with "The operation was canceled." stored and emailed as the site's build log.
    /// </summary>
    [Test]
    public async Task A_shutdown_mid_publish_is_an_interruption_not_a_failure_with_a_build_log()
    {
        var id = await DeploymentAsync(DeploymentStatus.Queued);
        var mail = new FakeEmailSender();
        var preparing = new TaskCompletionSource();

        await using var services = PublishServices(mail, preparing);
        var runner = Runner(services, new RunRegistry());

        await runner.StartAsync(CancellationToken.None);
        await preparing.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await runner.StopAsync(CancellationToken.None);

        await using var db = CreateContext();
        var row = await db.Deployments.SingleAsync(x => x.Id == id);

        Assert.Multiple(() =>
        {
            Assert.That(row.Status, Is.EqualTo(DeploymentStatus.Failed));
            Assert.That(row.Error, Is.EqualTo(DeploymentJobRunner.InterruptedError));
            Assert.That(row.ErrorDetail, Is.Null, "nothing was built, so there is no build log to show");
            Assert.That(mail.Sent, Has.Count.EqualTo(1));
            Assert.That(mail.Last.HtmlBody, Does.Contain("so nothing was published"));
            Assert.That(mail.Last.HtmlBody, Does.Not.Contain("canceled").And.Not.Contain("unexpectedly"));
        });
    }

    /// <summary>
    /// A publish cancelled because its site is being deleted stops, and says nothing to anybody: the owner has just
    /// removed the site, the row is about to go with it, and `DeleteSite` is waiting on <c>Finished</c> to take the
    /// published copy down after it. Before the runner worked on the run's own token nothing could stop a publish,
    /// and one that finished after the delete put the deleted site back online.
    /// </summary>
    [Test]
    public async Task A_publish_stopped_because_its_site_is_going_ends_without_telling_anyone()
    {
        var id = await DeploymentAsync(DeploymentStatus.Queued);
        var mail = new FakeEmailSender();
        var preparing = new TaskCompletionSource();
        var registry = new RunRegistry();

        await using var services = PublishServices(mail, preparing);
        var runner = Runner(services, registry);

        await runner.StartAsync(CancellationToken.None);
        await preparing.Task.WaitAsync(TimeSpan.FromSeconds(30));

        await using var db = CreateContext();
        var nanoid = (await db.Deployments.SingleAsync(x => x.Id == id)).Nanoid;
        var run = registry.Get(nanoid)!;

        registry.TryCancel(nanoid, run.UserId, CancelReason.SiteDeleted);
        await run.Finished.WaitAsync(TimeSpan.FromSeconds(30));
        await runner.StopAsync(CancellationToken.None);

        var row = await db.Deployments.AsNoTracking().SingleAsync(x => x.Id == id);

        Assert.Multiple(() =>
        {
            Assert.That(mail.Sent, Is.Empty, "no email about a site its owner has just deleted");
            Assert.That(row.Status, Is.Not.EqualTo(DeploymentStatus.Failed), "and no failure recorded for it");
        });
    }

    private ServiceProvider PublishServices(FakeEmailSender mail, TaskCompletionSource preparing) =>
        new ServiceCollection()
            .AddScoped(_ => CreateContext())
            .AddScoped<IDeploymentRepository, DeploymentRepository>()
            .AddScoped<IDomainRepository, DomainRepository>()
            .AddScoped<IUserRepository, UserRepository>()
            .AddSingleton<IEmailSender>(mail)
            .AddSingleton<IRunEventSink>(new SilentSink())
            .AddSingleton<ISandboxProvider>(new HangingProvider(preparing))
            .AddSingleton<ISiteRepositoryStore>(new GitSiteRepositoryStore(
                Options.Create(new RepositoryOptions { Root = Path.GetTempPath() }),
                NullLogger<GitSiteRepositoryStore>.Instance))
            .AddSingleton<IDeploymentTarget>(new FileSystemDeploymentTarget(
                Options.Create(new DeploymentOptions()),
                NullLogger<FileSystemDeploymentTarget>.Instance))
            .AddSingleton(Options.Create(new SitesOptions { BaseDomain = "webly.site" }))
            .AddSingleton(Options.Create(new AppOptions { BaseUrl = "https://localhost:5000" }))
            .AddSingleton<SiteMapper>()
            .BuildServiceProvider();

    private static DeploymentJobRunner Runner(ServiceProvider services, RunRegistry registry) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        registry,
        NullLogger<DeploymentJobRunner>.Instance);

    /// <summary>A build machine that never arrives, until Webly stops waiting for it.</summary>
    private sealed class HangingProvider(TaskCompletionSource preparing) : ISandboxProvider
    {
        public string Name => "hanging";

        public async Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken = default)
        {
            preparing.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);

            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class SilentSink : IRunEventSink
    {
        public Task EmitAsync(string runId, RunEvent runEvent, bool isTerminal = false, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>One deployment on a site of its own: a site has one live publish at a time, by index.</summary>
    private async Task<int> DeploymentAsync(DeploymentStatus status)
    {
        await using var db = CreateContext();

        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Owner" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var site = new Site { Name = "Koopman Cycles", Slug = $"koopman-{Guid.NewGuid():N}", OwnerId = user.Id };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        var version = new SiteVersion
        {
            SiteId = site.Id,
            CommitSha = new string('a', 40),
            Summary = "Created from the starter template",
            Origin = SiteVersionOrigin.Template,
            ChangedFileCount = 1,
            CreatedByUserId = user.Id
        };
        db.SiteVersions.Add(version);
        await db.SaveChangesAsync();

        var deployment = new Deployment
        {
            SiteId = site.Id,
            SiteVersionId = version.Id,
            TriggeredByUserId = user.Id,
            Status = status,
            StartedAt = status is DeploymentStatus.Queued ? null : DateTime.UtcNow
        };
        db.Deployments.Add(deployment);
        await db.SaveChangesAsync();

        return deployment.Id;
    }
}
