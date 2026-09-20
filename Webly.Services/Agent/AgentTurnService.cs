using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Webly.Data;
using Webly.Data.Models.Chat;
using Webly.Data.Repositories.Chat;
using Webly.Data.Repositories.Sites;
using Webly.Services.Agent.Agents;
using Webly.Services.Agent.Args;
using Webly.Services.Agent.Tools;
using Webly.Services.DTO.Realtime;
using Webly.Services.Services.Realtime;

namespace Webly.Services.Agent;

public interface IAgentTurnService
{
    /// <summary>
    /// Runs one turn and returns the conversation it happened in. Called on the run's own scope, by
    /// <see cref="ChatRunLauncher"/>, and never from a request thread.
    /// </summary>
    Task<string> RunAsync(BaseAgentArgs args, string message, int userId, CancellationToken cancellationToken);
}

/// <summary>
/// One turn, end to end: find the thread, write the person's message, hydrate the history, stream the agent's
/// answer, persist it, and commit whatever the turn changed as a single version.
///
/// The <b>order at the end</b> is the part worth reading. The assistant's message is written first so the version
/// can point at it, then the version is written, then the message is pointed back at the version — two saves,
/// because the pair of links is what makes the site's history readable as the conversation that produced it, and
/// neither row can hold the other's id before it exists.
/// </summary>
public class AgentTurnService(
    IServiceProvider serviceProvider,
    ISiteRepository siteRepository,
    IConversationRepository conversations,
    SiteEditSession session,
    RunWriter writer,
    WeblyDbContext dbContext,
    ILogger<AgentTurnService> logger) : IAgentTurnService
{
    /// <summary>
    /// How much of the thread the model sees. Enough to remember what the site is about and what was just asked
    /// for; the document itself is re-read through <c>ReadSite</c> every turn, so old tool traffic has no value —
    /// it describes a site that has since changed, which is worse than absent.
    /// </summary>
    private const int HistoryMessages = 30;

    public async Task<string> RunAsync(
        BaseAgentArgs args,
        string message,
        int userId,
        CancellationToken cancellationToken)
    {
        if (args is not SiteEditorAgentArgs siteArgs)
            throw new InvalidOperationException($"No agent handles arguments of type '{args.GetType().Name}'.");

        var site = await siteRepository.FindForOwnerLightAsync(siteArgs.SiteNanoid, userId, cancellationToken)
            ?? throw new InvalidOperationException($"Site '{siteArgs.SiteNanoid}' is not available to this user.");

        var conversation = await conversations.FindActiveAsync(site.Id, cancellationToken);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                SiteId = site.Id,
                UserId = userId,
                // Derived from the first message rather than asked of the model: a second provider call for a
                // label nobody reads twice.
                Title = message.Length <= 60 ? message : $"{message[..57]}..."
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
            Text = message
        };

        conversations.AddMessage(userMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        var agentKey = ResolveAgentKey(siteArgs);
        var agent = serviceProvider.GetKeyedService<AIAgent>(agentKey)
            // Falls back to the unsuffixed key so that an unknown model override degrades to the default model
            // rather than to an error — the person asked for an edit, not for a particular model.
            ?? serviceProvider.GetKeyedService<AIAgent>(SiteEditorAgent.Key)
            ?? throw new InvalidOperationException($"No agent is registered for key '{agentKey}'.");

        var answer = await StreamAsync(agent, history, message, cancellationToken);

        var assistantMessage = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = MessageRole.Assistant,
            Sequence = await conversations.NextSequenceAsync(conversation.Id, cancellationToken),
            Text = answer
        };

        conversations.AddMessage(assistantMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        var version = await session.CommitAsync(assistantMessage.Id, cancellationToken);

        if (version is not null)
        {
            assistantMessage.ProducedVersionId = version.Id;
            await dbContext.SaveChangesAsync(cancellationToken);

            // The editor refreshes its preview and its history from this, rather than polling for a change it
            // has no way to predict the timing of.
            await writer.WriteAsync(new RunEvent
            {
                Type = RunEventType.VersionCommitted,
                VersionNanoid = version.Nanoid,
                Detail = version.Summary
            }, cancellationToken);
        }

        return conversation.Nanoid;
    }

    /// <summary>
    /// Streams one agent response into the writer, turning each kind of update into the event the client knows
    /// how to draw.
    ///
    /// NOTE: the streaming shapes here are <c>Microsoft.Agents.AI</c> 1.9.0's and have not been run against the
    /// package yet — see whats_next.md. Everything around them (the writer, the sink, the log, the replay) is
    /// independent of that surface, which is why this is the one method to reconcile first.
    /// </summary>
    private async Task<string> StreamAsync(
        AIAgent agent,
        IReadOnlyList<ConversationMessage> history,
        string message,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();

        foreach (var past in history)
        {
            // Tool traffic is dropped: it is large, and it describes a document that has since moved on.
            if (past.Role is MessageRole.Tool) continue;

            messages.Add(new ChatMessage(RoleOf(past.Role), past.Text));
        }

        messages.Add(new ChatMessage(ChatRole.User, message));

        await foreach (var update in agent.RunStreamingAsync(messages, cancellationToken: cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent text when text.Text.Length > 0:
                        await writer.WriteTextAsync(text.Text, cancellationToken);
                        break;

                    case FunctionCallContent call:
                        await writer.WriteAsync(new RunEvent
                        {
                            Type = RunEventType.ToolCall,
                            Tool = call.Name,
                            Detail = ToolPhrases.For(call.Name)
                        }, cancellationToken);
                        break;

                    case FunctionResultContent result:
                        await writer.WriteAsync(new RunEvent
                        {
                            Type = RunEventType.ToolResult,
                            Tool = result.CallId,
                            Detail = Summarize(result.Result?.ToString())
                        }, cancellationToken);
                        break;
                }
            }
        }

        return await writer.CompleteMessageAsync(cancellationToken);
    }

    /// <summary>
    /// The key an args object routes to, plus its model override when it has one. One line per agent, and the
    /// reason the router is a switch rather than a lookup: adding an agent should not compile until this has been
    /// thought about.
    /// </summary>
    private static string ResolveAgentKey(BaseAgentArgs args)
    {
        var key = args switch
        {
            SiteEditorAgentArgs => SiteEditorAgent.Key,
            _ => throw new InvalidOperationException($"No agent handles '{args.GetType().Name}'.")
        };

        return string.IsNullOrWhiteSpace(args.Model) ? key : $"{key}-{args.Model.Trim().ToLowerInvariant()}";
    }

    private static ChatRole RoleOf(MessageRole role) => role switch
    {
        MessageRole.Assistant => ChatRole.Assistant,
        MessageRole.System => ChatRole.System,
        _ => ChatRole.User
    };

    /// <summary>A tool result as a chip caption: the first line, clipped. The full text is the model's business.</summary>
    private static string? Summarize(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;

        var firstLine = result.Split('\n')[0].Trim();

        return firstLine.Length <= 80 ? firstLine : $"{firstLine[..77]}...";
    }
}

/// <summary>
/// What a tool call looks like in the stream: "Reading your site", not "ReadSite". The person watching does not
/// know what a tool is, and a chip that reads like an internal name makes the product look like a debugger.
/// </summary>
public static class ToolPhrases
{
    private static readonly Dictionary<string, string> Phrases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ReadSite"] = "Reading your site",
        ["AddPage"] = "Adding a page",
        ["UpdatePage"] = "Updating a page",
        ["RemovePage"] = "Removing a page",
        ["AddSection"] = "Adding a section",
        ["UpdateSection"] = "Editing a section",
        ["MoveSection"] = "Moving a section",
        ["RemoveSection"] = "Removing a section",
        ["SetSectionHidden"] = "Hiding a section",
        ["SetTheme"] = "Changing the look",
        ["SetNavigation"] = "Updating the menu",
        ["AskUser"] = "Asking you something"
    };

    public static string For(string? tool) =>
        tool is not null && Phrases.TryGetValue(tool, out var phrase) ? phrase : "Working";
}
