namespace Webly.Services.Agent;

/// <summary>
/// The models Webly runs agents on, spelled once.
///
/// Opus is the default and the cheaper model is opt-in, deliberately: this agent is editing somebody's
/// business website in one pass with no review step, and the failure mode of a weaker model is not a worse
/// sentence — it is a broken build or an invented fact. Cost is the customer's decision to make through a
/// plan, not ours to make silently.
/// </summary>
public static class AgentModels
{
    /// <summary>The default. Effort is set to <c>xhigh</c>, which is what coding work wants.</summary>
    public const string Default = "claude-opus-5";

    /// <summary>The cheaper option, offered explicitly.</summary>
    public const string Economy = "claude-sonnet-5";
}

/// <summary>
/// Which agents exist in this deployment, and their credentials.
///
/// Not validated at startup: <b>no key is a valid configuration</b>. With neither agent configured the chat
/// is absent rather than broken — <c>/api/sites/{nanoid}/chat/status</c> reports it disabled and the client
/// hides it — while sites, publishing and domains still work, and the tests need no secrets.
/// </summary>
public class CodingAgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Which agent a request gets when it does not name one.</summary>
    public string Default { get; set; } = "claude-code";

    public string Model { get; set; } = AgentModels.Default;

    public ClaudeCodeOptions ClaudeCode { get; set; } = new();
    public OpenCodeOptions OpenCode { get; set; } = new();
    public MockOptions Mock { get; set; } = new();

    /// <summary>How long one turn may run before the sandbox kills it.</summary>
    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The agent with no model behind it. Off unless asked for, and it is the one agent whose absence is not
    /// about credentials: enabling it in a deployment would mean customers' sites being edited by a headline
    /// substitution.
    /// </summary>
    public class MockOptions
    {
        public bool Enabled { get; set; }
    }

    public class ClaudeCodeOptions
    {
        /// <summary>
        /// The key the CLI runs on, injected into the sandbox for the run. A platform-owned key, because the
        /// customer never hears which model edits their site — which also means it is a secret that leaves
        /// this process. See CLAUDE.md "The agent's credentials" for what that buys and what it costs.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// A Claude subscription instead of an API key, for a developer's own testing: the long-lived token
        /// <c>claude setup-token</c> prints, which the CLI reads from <c>CLAUDE_CODE_OAUTH_TOKEN</c>.
        ///
        /// <b>Development only, and refused at startup anywhere else</b> (<c>AddWeblySites</c>). A consumer
        /// subscription is licensed for the subscriber's own use; running customers' turns on it is not that, and a
        /// product built on Claude runs on API keys under commercial terms. When both are set the API key wins, so
        /// adding a real key never leaves a subscription quietly in charge.
        ///
        /// What <c>/admin/usage</c> records for a turn on it is the CLI's own figure, priced at the API's list price —
        /// what the turn would have cost, not what the subscription costs.
        /// </summary>
        public string OAuthToken { get; set; } = string.Empty;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(OAuthToken);
    }

    public class OpenCodeOptions
    {
        /// <summary>
        /// OpenCode is provider-agnostic, so its key may be for any of them; whichever provider the model
        /// string names has to be the one this key belongs to.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Provider-qualified, the way OpenCode names models.</summary>
        public string Model { get; set; } = "anthropic/claude-opus-5";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    }
}
