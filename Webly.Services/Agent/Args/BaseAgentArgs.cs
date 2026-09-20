using System.Text.Json.Serialization;

namespace Webly.Services.Agent.Args;

/// <summary>
/// What the client sends with a message to say which agent should answer it and in what scope.
///
/// Polymorphic on a discriminator rather than a string key on the request: the scope an agent needs is part of
/// the args type (a site editor needs a site nanoid; something else will need something else), so the router
/// cannot be handed a key with the wrong fields. One agent exists today and this still earns its place — the
/// alternative is a string parameter that every future agent has to reinterpret.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "__type")]
[JsonDerivedType(typeof(SiteEditorAgentArgs), nameof(SiteEditorAgentArgs))]
public abstract class BaseAgentArgs
{
    /// <summary>
    /// Optional model override, appended to the agent key to select a registered variant. Absent means the
    /// agent's own default.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Writes the turn's scope into the scoped context objects the toolkits read. Called once by the launcher,
    /// on the run's own scope, before the agent is resolved — which is how a tool knows which site it is
    /// editing without any tool taking a site id as a parameter the model could get wrong.
    /// </summary>
    public abstract void SetupScope(IServiceProvider serviceProvider);
}
