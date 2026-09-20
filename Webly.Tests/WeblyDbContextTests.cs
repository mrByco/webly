using Microsoft.EntityFrameworkCore;
using Webly.Data.Models.Authentication;
using Webly.Data.Models.Chat;
using Webly.Data.Models.Deployments;
using Webly.Data.Models.Sites;

namespace Webly.Tests;

/// <summary>
/// The context's own behaviour: the stamping every write depends on, and the delete cascades that decide
/// whether an account can be closed at all.
/// </summary>
public class WeblyDbContextTests : PostgresTestBase
{
    [Test]
    public async Task Inserting_stamps_a_nanoid_and_timestamps()
    {
        await using var db = CreateContext();

        var user = new User { Email = "a@example.com", DisplayName = "A" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(user.Nanoid, Is.Not.Empty);
            Assert.That(user.CreatedAt, Is.Not.EqualTo(default(DateTime)));
            Assert.That(user.UpdatedAt, Is.EqualTo(user.CreatedAt));
        });
    }

    [Test]
    public async Task An_explicitly_set_nanoid_is_left_alone()
    {
        await using var db = CreateContext();

        var user = new User { Email = "b@example.com", DisplayName = "B", Nanoid = "fixture-1" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        Assert.That(user.Nanoid, Is.EqualTo("fixture-1"));
    }

    /// <summary>
    /// The canary for the deferred foreign keys.
    ///
    /// A site points at two of its own versions, a version points at the message that produced it, and a
    /// deployment points at the version it published. Deleting the account has to take all of it in one
    /// statement — and Postgres checks a constraint after each triggered action rather than after all of
    /// them, so without <c>DEFERRABLE INITIALLY DEFERRED</c> on those references whichever cascade ran
    /// second would lose, and an account that had ever published would be undeletable.
    ///
    /// Do not change the delete behaviours in <c>WeblyDbContext</c> or the deferral in the migration
    /// without running this.
    /// </summary>
    [Test]
    public async Task Deleting_a_user_removes_their_sites_and_everything_under_them()
    {
        await using var db = CreateContext();

        var user = new User { Email = "owner@example.com", DisplayName = "Owner" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var site = new Site { OwnerId = user.Id, Name = "Bakery", Slug = "bakery" };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        var conversation = new Conversation { SiteId = site.Id, UserId = user.Id, Title = "First" };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var message = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Sequence = 1,
            Text = "Build me a bakery site"
        };

        db.ConversationMessages.Add(message);
        await db.SaveChangesAsync();

        var first = new SiteVersion
        {
            SiteId = site.Id,
            CommitSha = new string('a', 40),
            Summary = "Created from the starter template",
            Origin = SiteVersionOrigin.Template,
            ChangedFileCount = 12,
            CreatedByUserId = user.Id
        };

        db.SiteVersions.Add(first);
        await db.SaveChangesAsync();

        var second = new SiteVersion
        {
            SiteId = site.Id,
            ParentVersionId = first.Id,
            CommitSha = new string('b', 40),
            Summary = "Rewrote the hero",
            Origin = SiteVersionOrigin.Agent,
            ChangedFileCount = 2,
            CreatedByUserId = user.Id,
            SourceMessageId = message.Id
        };

        db.SiteVersions.Add(second);
        await db.SaveChangesAsync();

        message.ProducedVersionId = second.Id;
        site.HeadVersionId = second.Id;
        site.PublishedVersionId = first.Id;

        db.Domains.Add(new Domain { SiteId = site.Id, Hostname = "bakery.example.com", IsPrimary = true });
        db.Deployments.Add(new Deployment
        {
            SiteId = site.Id,
            SiteVersionId = first.Id,
            Status = DeploymentStatus.Ready,
            TriggeredByUserId = user.Id
        });

        await db.SaveChangesAsync();

        db.Users.Remove(user);
        await db.SaveChangesAsync();

        // MultipleAsync rather than Multiple: an async lambda handed to Multiple returns before its
        // awaits finish, so the assertions would run outside the block that collects them.
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await db.Sites.CountAsync(), Is.Zero);
            Assert.That(await db.SiteVersions.CountAsync(), Is.Zero);
            Assert.That(await db.Domains.CountAsync(), Is.Zero);
            Assert.That(await db.Deployments.CountAsync(), Is.Zero);
            Assert.That(await db.Conversations.CountAsync(), Is.Zero);
            Assert.That(await db.ConversationMessages.CountAsync(), Is.Zero);
        });
    }

    /// <summary>
    /// A site cannot be edited by two people at once today, but two "New chat" clicks can race. The
    /// partial unique index is what decides it, rather than a read-then-write check that both would pass.
    /// </summary>
    [Test]
    public async Task A_site_can_only_have_one_active_conversation()
    {
        await using var db = CreateContext();

        var user = new User { Email = "one@example.com", DisplayName = "One" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var site = new Site { OwnerId = user.Id, Name = "Site", Slug = "site-one" };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        db.Conversations.Add(new Conversation { SiteId = site.Id, UserId = user.Id });
        await db.SaveChangesAsync();

        db.Conversations.Add(new Conversation { SiteId = site.Id, UserId = user.Id });

        Assert.That(async () => await db.SaveChangesAsync(), Throws.TypeOf<DbUpdateException>());
    }

    /// <summary>
    /// The subdomain is how a site is reached before anybody buys a domain, so two sites sharing one is a
    /// site that cannot be published.
    /// </summary>
    [Test]
    public async Task Two_sites_cannot_share_a_slug()
    {
        await using var db = CreateContext();

        var user = new User { Email = "two@example.com", DisplayName = "Two" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.Sites.Add(new Site { OwnerId = user.Id, Name = "First", Slug = "taken" });
        await db.SaveChangesAsync();

        db.Sites.Add(new Site { OwnerId = user.Id, Name = "Second", Slug = "taken" });

        Assert.That(async () => await db.SaveChangesAsync(), Throws.TypeOf<DbUpdateException>());
    }

    /// <summary>
    /// One commit, one version row.
    ///
    /// There is no jsonb document any more — the content is a commit in the site's repository, and this row
    /// is the index into it. That makes the <c>(SiteId, CommitSha)</c> index load-bearing rather than
    /// tidy: <c>CommitSiteVersion</c> refuses to write a version when the tree did not change, and if a
    /// second row for the same commit could exist anyway, the history would show a change that is not one
    /// and a restore could pick the wrong row to copy forward.
    /// </summary>
    [Test]
    public async Task A_commit_appears_once_in_a_site_s_history()
    {
        await using var db = CreateContext();

        var user = new User { Email = "three@example.com", DisplayName = "Three" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var site = new Site { OwnerId = user.Id, Name = "Round", Slug = "round-trip" };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        var sha = new string('c', 40);

        db.SiteVersions.Add(new SiteVersion
        {
            SiteId = site.Id,
            CommitSha = sha,
            Summary = "Created from the starter template",
            Origin = SiteVersionOrigin.Template,
            CreatedByUserId = user.Id
        });

        await db.SaveChangesAsync();

        db.SiteVersions.Add(new SiteVersion
        {
            SiteId = site.Id,
            CommitSha = sha,
            Summary = "The same commit again",
            Origin = SiteVersionOrigin.Agent,
            CreatedByUserId = user.Id
        });

        Assert.That(async () => await db.SaveChangesAsync(), Throws.TypeOf<DbUpdateException>());
    }
}
