namespace Webly.Services.Agent;

/// <summary>
/// Model identifiers, spelled once. Copied from the reference project's own <c>AiName</c> so that the two
/// codebases name the same models the same way — a string literal in a factory is how a model upgrade turns
/// into a search-and-replace across files.
///
/// Short list on purpose. Webly has one agent and it does one hard thing (tool-calling over a structured
/// document), so the choice is "the good one" or "the cheap one" and a provider zoo would be six more
/// registrations nobody selects.
/// </summary>
public static class AiName
{
    /// <summary>The default. Tool-use quality on a long, messy instruction is the whole job here.</summary>
    public const string Sonnet_4_6 = "claude-sonnet-4-6";

    /// <summary>The cheap alternative, for the turns that are a single obvious edit.</summary>
    public const string Gpt5_4_Mini = "gpt-5.4-mini";
}
