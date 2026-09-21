using Microsoft.EntityFrameworkCore;
using Webly.Data.Models.Authentication;
using Webly.Data.Models.Chat;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Chat;

namespace Webly.Tests;

/// <summary>
/// Two turns on one site at the same moment — a second browser tab, a double-click, a client that retried.
///
/// Both of the things a turn writes before it does any work are check-then-act against a unique index: "is
/// there an open thread, if not make one" and "what is the next sequence number". Driven concurrently in the
/// running app, both lost: one turn failed with "something went wrong" on a message that was perfectly fine,
/// twice in a row for two different reasons. The indexes are right — they are what keeps a thread ordered and
/// a site to one open conversation — so the repository races against them instead of removing them.
///
/// These assert the outcome rather than the collision. A run in which nothing actually collided still passes;
/// a run in which the retry is broken cannot.
/// </summary>
public class ConversationConcurrencyTests : PostgresTestBase
{
    private int _siteId;

    [SetUp]
    public async Task CreateSite()
    {
        await using var db = CreateContext();

        var user = new User { Email = "owner@example.com", DisplayName = "Owner" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var site = new Site { Name = "Koopman Cycles", Slug = $"koopman-{Guid.NewGuid():N}", OwnerId = user.Id };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        _siteId = site.Id;
        UserId = user.Id;
    }

    private int UserId { get; set; }

    [Test]
    public async Task Ten_messages_appended_at_once_all_land_with_their_own_sequence()
    {
        await using var setup = CreateContext();

        var conversation = await new ConversationRepository(setup)
            .FindOrCreateActiveAsync(_siteId, UserId, "Concurrency");

        // Each on its own context, which is what two turns are: a scope each, a connection each.
        await Task.WhenAll(Enumerable.Range(0, 10).Select(async i =>
        {
            await using var db = CreateContext();

            await new ConversationRepository(db).AppendMessageAsync(new ConversationMessage
            {
                ConversationId = conversation.Id,
                Role = MessageRole.User,
                Text = $"message {i}"
            });
        }));

        await using var check = CreateContext();

        var sequences = await check.ConversationMessages
            .Where(x => x.ConversationId == conversation.Id)
            .Select(x => x.Sequence)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(sequences, Has.Count.EqualTo(10), "every message was written");
            Assert.That(sequences.Distinct().Count(), Is.EqualTo(10), "and none of them shares a number");
            Assert.That(sequences.Order(), Is.EqualTo(Enumerable.Range(1, 10)), "which leaves no gaps either");
        });
    }

    [Test]
    public async Task Five_turns_starting_at_once_share_one_thread()
    {
        // The index says a site has at most one open conversation. Before this, four of these five threw a
        // unique violation that reached the person as a failed message.
        var found = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var db = CreateContext();

            var conversation = await new ConversationRepository(db)
                .FindOrCreateActiveAsync(_siteId, UserId, "First message");

            return conversation.Id;
        }));

        await using var check = CreateContext();

        var rows = await check.Conversations.CountAsync(x => x.SiteId == _siteId);

        Assert.Multiple(() =>
        {
            Assert.That(found.Distinct().Count(), Is.EqualTo(1), "they all got the same thread");
            Assert.That(rows, Is.EqualTo(1), "and only one was written");
        });
    }
}
