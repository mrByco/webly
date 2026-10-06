using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Webly.Data;
using Webly.Data.Models.Authentication;
using Webly.Data.Models.Chat;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Chat;
using Webly.Services.Agent;
using Webly.Services.Services.Realtime;

namespace Webly.Tests;

/// <summary>
/// A turn the process died under. Nothing in a dead process can say how its turn ended, so the next process does,
/// as it starts: found by killing the backend while a turn was waking a site, after which the thread ended on the
/// person's own message for good.
/// </summary>
public class InterruptedTurnTests : PostgresTestBase
{
    [Test]
    public async Task Only_a_thread_ending_on_the_persons_own_message_is_noted_and_only_once()
    {
        var interrupted = await ThreadAsync(MessageRole.Assistant, MessageRole.User);
        var answered = await ThreadAsync(MessageRole.User, MessageRole.Assistant);
        var failed = await ThreadAsync(MessageRole.User, MessageRole.System);

        await SweepAsync();
        await SweepAsync();

        await using var db = CreateContext();

        var last = await LastAsync(db, interrupted);
        var counts = (await CountAsync(db, interrupted), await CountAsync(db, answered), await CountAsync(db, failed));

        Assert.Multiple(() =>
        {
            Assert.That(
                last,
                Is.EqualTo((MessageRole.System, AgentTurnService.InterruptedNote)),
                "the cut-off turn says what happened");
            Assert.That(counts.Item1, Is.EqualTo(3), "once, however many times the app starts");
            Assert.That(counts.Item2, Is.EqualTo(2), "a finished turn is left alone");
            Assert.That(counts.Item3, Is.EqualTo(2), "and so is one that already said how it ended");
        });
    }

    private async Task SweepAsync()
    {
        await using var services = new ServiceCollection()
            .AddScoped(_ => CreateContext())
            .AddScoped<IConversationRepository, ConversationRepository>()
            .BuildServiceProvider();

        await new InterruptedTurnSweeper(
                services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<InterruptedTurnSweeper>.Instance)
            .StartAsync(CancellationToken.None);
    }

    /// <summary>A site with one thread holding these messages in this order. One site each: a site has one open thread.</summary>
    private async Task<int> ThreadAsync(params MessageRole[] roles)
    {
        await using var db = CreateContext();

        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Owner" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var site = new Site { Name = "Koopman Cycles", Slug = $"koopman-{Guid.NewGuid():N}", OwnerId = user.Id };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        var repository = new ConversationRepository(db);
        var conversation = await repository.FindOrCreateActiveAsync(site.Id, user.Id, "Hours");

        foreach (var role in roles)
            await repository.AppendMessageAsync(
                new ConversationMessage { ConversationId = conversation.Id, Role = role, Text = $"{role} speaking" });

        return conversation.Id;
    }

    private static async Task<(MessageRole, string)> LastAsync(WeblyDbContext db, int conversationId)
    {
        var last = await db.ConversationMessages
            .Where(x => x.ConversationId == conversationId)
            .OrderByDescending(x => x.Sequence)
            .FirstAsync();

        return (last.Role, last.Text);
    }

    private static Task<int> CountAsync(WeblyDbContext db, int conversationId) =>
        db.ConversationMessages.CountAsync(x => x.ConversationId == conversationId);
}
