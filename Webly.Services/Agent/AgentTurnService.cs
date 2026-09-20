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
    ISiteRepositoryStore repositories,
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

        var agent = agents.Resolve(request.Agent);

        await using var lease = await workspaces.AcquireAsync(
            site,
            progress => writer.WriteAsync(
                new RunEvent { Type = RunEventType.WorkspaceProgress, Detail = progress }, cancellationToken),
            cancellationToken);

        var workspace = lease.Workspace;
        var changedFiles = new HashSet<string>(StringComparer.Ordinal);

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

        await ReportBuildErrorsAsync(workspace, cancellationToken);

        return conversation.Nanoid;
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
    /// Shows the person a compile error if the turn left one.
    ///
    /// The dev server is the build gate that the editor can see, and a Next.js compile error is both the most
    /// useful thing to put on screen and the exact text the next message should carry back to the agent. Read
    /// rather than inferred: the agent may sincerely believe it is finished.
    /// </summary>
    private async Task ReportBuildErrorsAsync(SiteWorkspace workspace, CancellationToken cancellationToken)
    {
        var log = await workspace.Sandbox.ReadDevServerLogAsync(cancellationToken);

        if (log.Length == 0) return;

        var hasError = log.Contains("Failed to compile", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Module not found", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Type error:", StringComparison.OrdinalIgnoreCase);

        if (!hasError) return;

        await writer.WriteAsync(new RunEvent
        {
            Type = RunEventType.BuildFailed,
            Detail = log.Length <= 2000 ? log : log[^2000..]
        }, cancellationToken);
    }

    private static string Describe(ConversationMessage message) =>
        $"{(message.Role == MessageRole.User ? "They asked" : "You replied")}: {message.Text}";
}
