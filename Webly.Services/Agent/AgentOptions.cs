namespace Webly.Services.Agent;

/// <summary>
/// The editor agent's credentials, and nothing else — which model it uses is a constant in the factory, not a
/// setting, because a model change is a behaviour change that deserves a commit.
///
/// Not validated at startup, deliberately: <b>no key is a valid configuration</b>. With neither provider
/// configured the agent is not registered, <c>GET /api/agent/status</c> reports it disabled and the client hides
/// the chat — so the rest of the product, including publishing, still works and the tests need no secrets. Same
/// discipline as Google sign-in.
/// </summary>
public class AgentOptions
{
    public const string SectionName = "Agent";

    public ProviderOptions Anthropic { get; set; } = new();
    public ProviderOptions OpenAi { get; set; } = new();

    public bool IsConfigured => Anthropic.IsConfigured || OpenAi.IsConfigured;

    public class ProviderOptions
    {
        public string ApiKey { get; set; } = string.Empty;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    }
}
