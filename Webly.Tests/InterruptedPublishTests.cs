using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Models.Deployments;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Deployments;
using Webly.Data.Repositories.Users;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Email;
using Webly.Services.Services.Realtime;

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
