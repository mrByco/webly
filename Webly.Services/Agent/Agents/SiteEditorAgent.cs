using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Webly.Services.Agent.Tools;
using Webly.Services.Services.Sites;

namespace Webly.Services.Agent.Agents;

/// <summary>
/// The one agent: a website editor with write access to a site document.
///
/// A static <c>Create(IServiceProvider, key)</c> factory registered keyed, so the runner only ever holds an
/// <c>AIAgent</c> and never knows which one it has — the pattern taken from the reference project, and the reason
/// a second agent (or a multi-agent workflow behind <c>.AsAIAgent()</c>) costs one registration and no change to
/// the run substrate.
/// </summary>
public static class SiteEditorAgent
{
    public const string Key = "site-editor";

    /// <summary>
    /// The model, as a constant rather than a setting. Changing which model edits people's websites is a
    /// behaviour change, and it should be a commit somebody can read — not an environment variable that makes the
    /// same build behave differently in two deployments.
    /// </summary>
    public const string Model = AiName.Sonnet_4_6;

    public static AIAgent Create(IServiceProvider serviceProvider, string key)
    {
        var chatClient = serviceProvider.GetRequiredKeyedService<IChatClient>(ModelFor(key));

        var site = serviceProvider.GetRequiredService<SiteToolkit>();
        var questions = serviceProvider.GetRequiredService<QuestionToolkit>();

        return new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "Webly site editor",
            Instructions = Instructions,
            ChatOptions = new ChatOptions
            {
                Tools =
                [
                    AIFunctionFactory.Create(site.ReadSite),
                    AIFunctionFactory.Create(site.AddPage),
                    AIFunctionFactory.Create(site.UpdatePage),
                    AIFunctionFactory.Create(site.RemovePage),
                    AIFunctionFactory.Create(site.AddSection),
                    AIFunctionFactory.Create(site.UpdateSection),
                    AIFunctionFactory.Create(site.MoveSection),
                    AIFunctionFactory.Create(site.RemoveSection),
                    AIFunctionFactory.Create(site.SetSectionHidden),
                    AIFunctionFactory.Create(site.SetTheme),
                    AIFunctionFactory.Create(site.SetNavigation),
                    AIFunctionFactory.Create(questions.AskUser)
                ]
            }
        });
    }

    /// <summary>
    /// A variant key is <c>site-editor-{model}</c>, which is how an alternative model is offered without a second
    /// agent class. An unsuffixed key is the default model.
    /// </summary>
    private static string ModelFor(string key) =>
        key.Length > Key.Length + 1 && key.StartsWith($"{Key}-", StringComparison.Ordinal)
            ? key[(Key.Length + 1)..]
            : Model;

    /// <summary>
    /// The instructions, with the section catalogue appended from <see cref="SectionCatalogue.Describe"/> rather
    /// than written out here. A prompt that lists fields by hand is a second declaration of the schema, and it is
    /// the one that goes stale — a field added to the catalogue would be a field the model never uses and a
    /// sentence in the prompt nobody notices is wrong.
    /// </summary>
    private static string Instructions =>
        $"""
        You are the editor of one website, inside Webly. The person you are talking to owns it and is not a
        developer: they will never see HTML, and they cannot fix anything you get wrong by editing code. Your
        edits are what their site is.

        How to work:

        - Read the site before changing it, unless you already know its structure from this conversation.
        - Make the edits the person asked for, then say what you changed in one or two plain sentences. Do not
          describe your tool calls, and do not show JSON.
        - Prefer editing what is there over replacing it. A person who asked for a new headline has not asked you
          to restructure their home page.
        - Write copy in the language the person writes to you in, for their customers rather than about their
          company: what the reader gets, not how passionate the team is.

        What you must never do:

        - Never invent a fact about their business — hours, prices, addresses, phone numbers, years in business,
          customer quotes, or anything a visitor could act on and be wrong. Use AskUser. A plausible invention on
          a real website is the worst outcome of this product.
        - Never write a testimonial that nobody said. If they have none yet, leave the section out and say why.
        - Never claim you published anything. Publishing is a button the person presses; you change the draft.

        One turn is one version of the site, with one summary line in their history. Several small edits in a turn
        are normal and cost nothing.

        The sections you can build pages from, and their exact fields:

        {SectionCatalogue.Describe()}
        """;
}
