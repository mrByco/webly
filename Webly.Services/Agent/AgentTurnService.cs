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
using Webly.Services.Services.Sandboxes;
using Webly.Services.Services.Workspaces;
using Webly.Services.UseCases.Sites;

namespace Webly.Services.Agent;

public interface IAgentTurnService
{
    /// <summary>
    /// Runs one turn. Called on the run's own scope, by <see cref="ChatRunLauncher"/>, and never from a
    /// request thread.
    ///
    /// It returns nothing, deliberately. It used to return the conversation's nanoid, and the launcher
    /// assigned the run's correlation id from it — which is the same value, arriving far too late to be the
    /// thing a reloading page looks the run up by. Taking the return value away means nobody can make that
    /// mistake twice; the nanoid is reported through <paramref name="onConversation"/>, when it is true.
    /// </summary>
    /// <param name="onConversation">
    /// Called with the conversation's nanoid as soon as there is one, which is before any of the work.
    ///
    /// It exists because the return value is too late to be useful: a page that reloads mid-turn finds the
    /// run by asking which one is attached to this conversation, and a correlation set when the turn
    /// <i>finishes</i> is never there while it is running. The whole "a run outlives the connection that
    /// started it" design hangs off this line — without it a reload shows a finished-looking thread with a
    /// turn still writing files behind it.
    /// </param>
    Task RunAsync(
        SendMessageRequest request,
        int userId,
        Action<string> onConversation,
        CancellationToken cancellationToken);
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
    SyncWeblyOwnedFiles syncOwnedFiles,
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

    /// <summary>
    /// How much of a compiler's opinion reaches the chat. Long enough for an error with its import trace, short
    /// enough that the thread is still a conversation.
    /// </summary>
    private const int DetailLimit = 2000;

    public async Task RunAsync(
        SendMessageRequest request,
        int userId,
        Action<string> onConversation,
        CancellationToken cancellationToken)
    {
        var site = await siteRepository.FindForOwnerAsync(request.SiteNanoid, userId, cancellationToken)
            ?? throw new InvalidOperationException($"Site '{request.SiteNanoid}' is not available to this user.");

        var user = await users.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} disappeared mid-turn.");

        // Found or created in one call, because two turns starting at the same moment — a second tab, a
        // double-click — both see no thread, both insert, and the index that says a site has one open thread
        // refuses the loser. That reached the person as "something went wrong" on a message that was fine.
        var conversation = await conversations.FindOrCreateActiveAsync(
            site.Id,
            userId,
            // Derived from the first message rather than asked of the model: a second provider call for a
            // label nobody reads twice.
            request.Message.Length <= 60 ? request.Message : $"{request.Message[..57]}...",
            cancellationToken);

        // Before anything that takes time, because this is what a reload looks the run up by.
        onConversation(conversation.Nanoid);

        var history = await conversations.ListMessagesAsync(conversation.Id, HistoryMessages, cancellationToken);

        var userMessage = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Text = request.Message
        };

        await conversations.AppendMessageAsync(userMessage, cancellationToken);

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
        catch (RepositoryConflictException conflict)
        {
            // The one failure whose own sentence is better than ours: something else changed the site while this
            // turn was working, so nothing was written. "Something went wrong" would be true and useless — the
            // person knows what else they did, and asking again is all this needs.
            await NoteAsync(conversation.Id, conflict.Message);
            throw;
        }
        catch (Exception)
        {
            await NoteAsync(conversation.Id, FailureNote);
            throw;
        }
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

        // Before the workspace, so the sandbox is seeded with the instructions the agent is about to read.
        // `AGENTS.md` lives in the site's repository, so a site created before a rule changed still carries the
        // old one — and the rules are there to stop the agent doing exactly the things it would otherwise
        // confidently do. Its own version, never folded into this turn's: see SyncWeblyOwnedFiles.
        await syncOwnedFiles.ExecuteAsync(
            site, new CommitAuthor(user.DisplayName, user.Email), user.Id, cancellationToken);

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
            Text = reply
        };

        await conversations.AppendMessageAsync(assistantMessage, cancellationToken);

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

        await ReportBreakageAsync(workspace, logOffset, version is not null, cancellationToken);
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
            await conversations.AppendMessageAsync(
                new ConversationMessage
                {
                    ConversationId = conversationId,
                    Role = MessageRole.System,
                    Text = text
                },
                CancellationToken.None);
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

        // The site row as it is *now*, not as it was when this turn started: another turn on the same site can
        // have committed while this one queued on the workspace lease — a second tab, a double-click — and the
        // row this one records has to hang off the version that is really the branch.
        await dbContext.Entry(site).ReloadAsync(cancellationToken);
        await dbContext.Entry(site).Reference(x => x.HeadVersion).LoadAsync(cancellationToken);

        // And `workspace.CommitSha` — the commit the sandbox's tree was actually seeded from — is what git is
        // given to check the branch against. Those two are the same thing for a turn that queued behind
        // another, because acquiring the lease re-seeds a workspace whose head has moved. They are **not** the
        // same when the branch moves while this turn holds the lease, which an upload does: it commits from the
        // repository and its re-seed waits politely for the lease it cannot have.
        //
        // Reloading the row and using *that* as the expected value is what this code used to do, and it turned
        // the guard into a formality: the two were equal by construction, git had nothing to refuse, and the
        // turn's commit — carrying the tree from before the upload — deleted the photograph that had just been
        // added, with "Added probe.png" sitting in the history directly above it. Found by uploading an image
        // three seconds after sending a message and then listing `public/images` at the head commit.
        var version = await commitSiteVersion.ExecuteAsync(
            site,
            tree,
            new CommitAuthor(user.DisplayName, user.Email),
            user.Id,
            SiteVersionOrigin.Agent,
            outcome.Summary,
            outcome.Details,
            sourceMessageId,
            treeBaseSha: workspace.CommitSha,
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
    /// Shows the person a compile error if <i>this turn</i> left one, from the two places one can hide.
    ///
    /// The dev server is the build gate the editor can see, and a Next.js compile error is both the most useful
    /// thing to put on screen and the exact text the next message should carry back to the agent. But it only sees
    /// half: <c>next dev</c> compiles with SWC, which strips types rather than checking them, so a type error
    /// serves a page happily and shows up nowhere until somebody presses Publish and gets an email saying the build
    /// failed. So the types are checked too, by the same command <c>AGENTS.md</c> asks the agent to run.
    ///
    /// <b>Read rather than inferred</b> — the agent may sincerely believe it is finished, and may sincerely believe
    /// it ran the typecheck.
    ///
    /// <paramref name="since"/> is the whole reason the first half is not a one-line check. The log is cumulative
    /// for the life of the dev server, so reading all of it would find the error a turn three messages ago left
    /// behind and report it again — telling somebody their site is broken every time they speak to it, no matter
    /// how many times they have it fixed. Only output that arrived after the turn began can say anything about the
    /// turn.
    /// </summary>
    /// <param name="committed">
    /// Whether the turn actually changed the site. A turn that changed nothing cannot have broken anything, and the
    /// typecheck is the one check here that costs real seconds — so it is not run for a conversation that was a
    /// question. It is also the honest cut for the other reason: <c>tsc</c> has no offset to read from, it only
    /// answers about the tree as it stands now, so the claim it supports is "what this turn produced does not
    /// compile" rather than "this turn broke it". Those differ when an earlier turn left the error, and the person's
    /// next move is the same either way.
    /// </param>
    private async Task ReportBreakageAsync(
        SiteWorkspace workspace,
        long since,
        bool committed,
        CancellationToken cancellationToken)
    {
        // Asked to compile before being asked what happened. `next dev` compiles on demand, so at this moment it
        // has not looked at the agent's edits at all — and the log would be empty, which reads as success. This
        // is the request that makes the question mean something; the editor's iframe would eventually send one,
        // but not before this check ran.
        await workspace.Sandbox.TouchPreviewAsync(cancellationToken);

        var log = await workspace.Sandbox.ReadDevServerLogAsync(since, cancellationToken);

        if (log.Text.Length > 0 && CompilerOutput.SaysTheBuildBroke(log.Text))
        {
            var detail = CompilerOutput.Readable(log.Text);

            await ReportAsync(detail.Length <= DetailLimit ? detail : detail[^DetailLimit..], cancellationToken);

            // One block, not two. A file the compiler could not parse is a file tsc cannot check either, so it
            // would report the same breakage in its own words — and two warning blocks about one mistake reads as
            // two mistakes.
            return;
        }

        if (!committed) return;

        await ReportTypeErrorsAsync(workspace, cancellationToken);
    }

    /// <summary>
    /// Runs the site's own typecheck and puts what it says on screen.
    ///
    /// <c>--silent</c> so npm does not narrate itself into the middle of the answer, and a timeout because this is
    /// on the path of every turn that changed something: <c>tsc</c> on a starter site takes about two seconds, and
    /// a check that hung would hold a turn open for as long as the sandbox allows.
    ///
    /// <b>A failure that does not name a type error is Webly's, not the site's.</b> A missing
    /// <c>node_modules</c>, a script that is not there, an <c>npm</c> that could not start — all exit non-zero, and
    /// telling a customer their site does not compile because our sandbox is wrong is worse than saying nothing.
    /// So it is logged for us and the chat stays quiet.
    /// </summary>
    private async Task ReportTypeErrorsAsync(SiteWorkspace workspace, CancellationToken cancellationToken)
    {
        var check = await workspace.Sandbox.RunAsync(
            new SandboxCommand("npm", ["run", "--silent", "typecheck"], TimeSpan.FromMinutes(2)),
            cancellationToken: cancellationToken);

        if (check.Succeeded) return;

        if (!CompilerOutput.SaysTheTypesBroke(check.Output))
        {
            logger.LogWarning(
                "The typecheck in {Sandbox} exited {Code} without naming a type error, so nothing was reported: {Output}",
                workspace.Sandbox.Id,
                check.ExitCode,
                check.Output);

            return;
        }

        await ReportAsync(CompilerOutput.TypeErrors(check.Output), cancellationToken);
    }

    private Task ReportAsync(string detail, CancellationToken cancellationToken) =>
        writer.WriteAsync(new RunEvent { Type = RunEventType.BuildFailed, Detail = detail }, cancellationToken);

    private static string Describe(ConversationMessage message) =>
        $"{(message.Role == MessageRole.User ? "They asked" : "You replied")}: {message.Text}";
}
