using Microsoft.Extensions.Logging;
using Webly.Data;
using Webly.Data.Models.Chat;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Chat;
using Webly.Data.Repositories.Sites;
using Webly.Data.Repositories.Users;
using Webly.Services.DTO.Chat;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services.Realtime;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Workspaces;
using Webly.Services.UseCases.Sites;

namespace Webly.Services.Agent;

public interface IAgentTurnService
{
    /// <summary>
    /// Runs one turn and returns the conversation it happened in. Called on the run's own scope, by
    /// <see cref="ChatRunLauncher"/>, and never from a request thread.
    /// </summary>
    Task<string> RunAsync(SendMessageRequest request, int userId, CancellationToken cancellationToken);
}

/// <summary>
/// One turn, end to end: get the site's workspace, let a coding agent work in it, and commit whatever it
/// changed as one version.
///
/// <b>The agent edits; Webly commits.</b> The agent never sees a git history and has no credential that could
/// write one — it changes files in a sandbox, and afterwards this service reads the tree back and asks the
/// repository store to commit it. Three things follow, and they are why it is arranged this way:
///
/// <list type="bullet">
/// <item>A turn is atomic and its history is honest. One turn is one commit with one subject line, and a turn
/// that failed halfway leaves the branch where it was.</item>
/// <item>History cannot be rewritten, by anyone. There is no amend, no force-push and no rebase available to
/// an agent that does not have the repository.</item>
/// <item>A cancelled turn costs nothing but tokens. Stop is honest: the sandbox's tree is discarded on the
/// next seed, and nothing was committed.</item>
/// </list>
///
/// The order at the end matters: the agent's message is written first so the version can point at it, then the
/// version, then the message is pointed back at the version. Neither row can hold the other's id before it
/// exists, and that pair of links is what makes the site's history read as the conversation that produced it.
/// </summary>
public class AgentTurnService(
    ISiteRepository siteRepository,
    IConversationRepository conversations,
    IUserRepository users,
    ISiteWorkspaceRegistry workspaces,
    CodingAgentRegistry agents,
    CommitSiteVersion commitSiteVersion,
    RunWriter writer,
    WeblyDbContext dbContext,
    ILogger<AgentTurnService> logger) : IAgentTurnService
{
    /// <summary>
    /// How much of the thread the agent is told about when it has no session of its own to resume. Short,
    /// because the codebase is the context that matters and the agent reads that itself — old chat turns
    /// describe a site that has since changed.
    /// </summary>
    private const int HistoryMessages = 8;

    /// <summary>
    /// What a failed turn says, in the thread and in the run's terminal event alike — <see cref="ChatRunLauncher"/>
    /// reads it from here so the two cannot drift. "Nothing was changed" is a promise the design keeps rather than a
    /// reassurance: the commit is the last step, so a turn that threw left the branch exactly where it was.
    /// </summary>
    public const string FailureNote =
        "Something went wrong while editing your site. Nothing was changed — please try again.";

    /// <summary>What a stopped turn says. Not an error: the person asked for it, and nothing was committed.</summary>
    public const string StoppedNote = "Stopped. Nothing was changed.";

    public async Task<string> RunAsync(SendMessageRequest request, int userId, CancellationToken cancellationToken)
    {
        var site = await siteRepository.FindForOwnerAsync(request.SiteNanoid, userId, cancellationToken)
            ?? throw new InvalidOperationException($"Site '{request.SiteNanoid}' is not available to this user.");

        var user = await users.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} disappeared mid-turn.");

        var conversation = await conversations.FindActiveAsync(site.Id, cancellationToken);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                SiteId = site.Id,
                UserId = userId,
                // Derived from the first message rather than asked of the model: a second provider call for a
                // label nobody reads twice.
                Title = request.Message.Length <= 60 ? request.Message : $"{request.Message[..57]}..."
            };

            conversations.Add(conversation);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var history = await conversations.ListMessagesAsync(conversation.Id, HistoryMessages, cancellationToken);

        var userMessage = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Sequence = await conversations.NextSequenceAsync(conversation.Id, cancellationToken),
            Text = request.Message
        };

        conversations.AddMessage(userMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        // From here the turn reaches a sandbox, another product's CLI and a git repository, so it can fail for
        // reasons this app does not control. However it ends, the thread has to end up describing it: the person's
        // message is already saved, and a thread whose last entry is their own sentence reads as "it ignored me".
        try
        {
            await RunTurnAsync(site, user, conversation, history, request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await NoteAsync(conversation.Id, StoppedNote);
            throw;
        }
        catch (Exception)
        {
            await NoteAsync(conversation.Id, FailureNote);
            throw;
        }

        return conversation.Nanoid;
    }

    /// <summary>
    /// The turn itself, once the person's message is saved. Split out so that <see cref="RunAsync"/> says what
    /// happens when it throws in one place, rather than around each of the six things that can.
    /// </summary>
    private async Task RunTurnAsync(
        Site site,
        Data.Models.Authentication.User user,
        Conversation conversation,
        IReadOnlyList<ConversationMessage> history,
        SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var agent = agents.Resolve(request.Agent);

        await using var lease = await workspaces.AcquireAsync(
            site,
            progress => writer.WriteAsync(
                new RunEvent { Type = RunEventType.WorkspaceProgress, Detail = progress }, cancellationToken),
            cancellationToken);

        var workspace = lease.Workspace;
        var changedFiles = new HashSet<string>(StringComparer.Ordinal);

        // Where the dev server's output has reached before this turn touches anything. Read now so that the
        // compile-error check at the end can look at what *this* turn caused — the log is cumulative, and
        // without this a single broken turn would make every later turn report the same error for ever.
        var logOffset = (await workspace.Sandbox.ReadDevServerLogAsync(cancellationToken: cancellationToken)).Offset;

        // The agent's own session is only resumable if it belongs to this agent: the person may have switched,
        // and handing one agent another's session id is nonsense rather than an optimization.
        var sessionId = workspace.AgentKey == agent.Key ? workspace.AgentSessionId : null;

        var outcome = await agent.RunAsync(
            workspace.Sandbox,
            new CodingAgentRequest(
                request.Message,
                sessionId is null ? [.. history.Select(Describe)] : [],
                sessionId),
            async agentEvent =>
            {
                switch (agentEvent)
                {
                    case CodingAgentEvent.Text text:
                        await writer.WriteTextAsync(text.Value, cancellationToken);
                        break;

                    case CodingAgentEvent.Activity activity:
                        await writer.WriteAsync(new RunEvent
                        {
                            Type = RunEventType.Activity,
                            Detail = activity.Phrase
                        }, cancellationToken);
                        break;

                    case CodingAgentEvent.FileChanged file:
                        // De-duplicated: an agent editing one file four times is one line in the UI, not four.
                        if (changedFiles.Add(file.Path))
                            await writer.WriteAsync(new RunEvent
                            {
                                Type = RunEventType.FileChanged,
                                Detail = file.Path
                            }, cancellationToken);

                        break;
                }
            },
            cancellationToken);

        workspace.AgentSessionId = outcome.SessionId;
        workspace.AgentKey = agent.Key;

        var reply = await writer.CompleteMessageAsync(cancellationToken);

        // The agent's own final text wins over the accumulated deltas: a stream can be partial, and the outcome
        // is what the CLI says it said.
        if (outcome.Reply.Length > 0) reply = outcome.Reply;

        var assistantMessage = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = MessageRole.Assistant,
            Sequence = await conversations.NextSequenceAsync(conversation.Id, cancellationToken),
            Text = reply
        };

        conversations.AddMessage(assistantMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        var version = await CommitAsync(site, workspace, user, outcome, assistantMessage.Id, cancellationToken);

        if (version is not null)
        {
            assistantMessage.ProducedVersionId = version.Id;
            await dbContext.SaveChangesAsync(cancellationToken);

            await writer.WriteAsync(new RunEvent
            {
                Type = RunEventType.VersionCommitted,
                VersionNanoid = version.Nanoid,
                Detail = version.Summary
            }, cancellationToken);
        }

        await ReportBuildErrorsAsync(workspace, logOffset, cancellationToken);
    }

    /// <summary>
    /// Writes what the app has to say about a turn that did not finish, as a <see cref="MessageRole.System"/> line
    /// in the thread — the same sentence the run's terminal event carries, so a reload tells the person what the
    /// live screen told them.
    ///
    /// <see cref="CancellationToken.None"/> throughout, because the usual reason to be here is that the turn's own
    /// token has just tripped. Its own try/catch, because the failure being reported may itself have been the
    /// database: a note that cannot be written is worth a log line and nothing more, and must not replace the
    /// exception on its way up with one about failing to describe it.
    /// </summary>
    private async Task NoteAsync(int conversationId, string text)
    {
        try
        {
            conversations.AddMessage(new ConversationMessage
            {
                ConversationId = conversationId,
                Role = MessageRole.System,
                Sequence = await conversations.NextSequenceAsync(conversationId, CancellationToken.None),
                Text = text
            });

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception, "Could not record the outcome of a turn in conversation {Conversation}.", conversationId);
        }
    }

    private async Task<SiteVersion?> CommitAsync(
        Site site,
        SiteWorkspace workspace,
        Data.Models.Authentication.User user,
        CodingAgentOutcome outcome,
        int sourceMessageId,
        CancellationToken cancellationToken)
    {
        var tree = await workspace.Sandbox.ReadTreeAsync(cancellationToken);

        var version = await commitSiteVersion.ExecuteAsync(
            site,
            tree,
            new CommitAuthor(user.DisplayName, user.Email),
            user.Id,
            SiteVersionOrigin.Agent,
            outcome.Summary,
            outcome.Details,
            sourceMessageId,
            cancellationToken: cancellationToken);

        if (version is null)
        {
            logger.LogInformation("Turn on {Site} changed nothing; no commit.", site.Nanoid);

            return null;
        }

        // The workspace is now at the commit it just produced, so the next turn's parent is this one rather than
        // a re-seed. Without this the registry would think the tree was stale and copy it back over itself.
        workspace.CommitSha = version.CommitSha;

        return version;
    }

    /// <summary>
    /// Shows the person a compile error if <i>this turn</i> left one.
    ///
    /// The dev server is the build gate that the editor can see, and a Next.js compile error is both the most
    /// useful thing to put on screen and the exact text the next message should carry back to the agent. Read
    /// rather than inferred: the agent may sincerely believe it is finished.
    ///
    /// <paramref name="since"/> is the whole reason this is not a one-line check. The log is cumulative for the
    /// life of the dev server, so reading all of it would find the error a turn three messages ago left behind
    /// and report it again — telling somebody their site is broken every time they speak to it, no matter how
    /// many times they have it fixed. Only output that arrived after the turn began can say anything about the
    /// turn.
    /// </summary>
    private async Task ReportBuildErrorsAsync(
        SiteWorkspace workspace,
        long since,
        CancellationToken cancellationToken)
    {
        // Asked to compile before being asked what happened. `next dev` compiles on demand, so at this moment it
        // has not looked at the agent's edits at all — and the log would be empty, which reads as success. This
        // is the request that makes the question mean something; the editor's iframe would eventually send one,
        // but not before this check ran.
        await workspace.Sandbox.TouchPreviewAsync(cancellationToken);

        var log = await workspace.Sandbox.ReadDevServerLogAsync(since, cancellationToken);
        var text = log.Text;

        if (text.Length == 0) return;

        if (!DevServerLogReader.SaysTheBuildBroke(text)) return;

        var detail = DevServerLogReader.Readable(text);

        await writer.WriteAsync(new RunEvent
        {
            Type = RunEventType.BuildFailed,
            Detail = detail.Length <= 2000 ? detail : detail[^2000..]
        }, cancellationToken);
    }

    private static string Describe(ConversationMessage message) =>
        $"{(message.Role == MessageRole.User ? "They asked" : "You replied")}: {message.Text}";
}
