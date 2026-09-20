using Anthropic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using Webly.Services.Agent;
using Webly.Services.Agent.Agents;

namespace Webly.Api.Extensions;

/// <summary>
/// Registers the chat clients and the agent, keyed by model name, and <b>only for the providers that have a key</b>.
///
/// That last part is the rule the rest of the design leans on: with no keys nothing is registered, the agent is absent
/// rather than broken, <c>/api/sites/{nanoid}/chat/status</c> reports it disabled, and the tests need no secrets — the
/// same discipline as Google sign-in. An agent whose model has no registered client is not registered either, so the
/// failure is a missing key at startup rather than a null client mid-turn.
/// </summary>
public static class AddAgentClients
{
    /// <summary>
    /// How long one call to a provider may take. The SDK defaults are around 100 seconds, which is right for a chat
    /// reply and too tight for a tool-calling turn that reads a site and rewrites three sections before its first
    /// token — the reference project lost whole authoring runs to exactly that timeout, four retries deep.
    /// </summary>
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddWeblyAgentClients(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new AgentOptions();
        configuration.GetSection(AgentOptions.SectionName).Bind(options);

        services.AddOptions<AgentOptions>().Bind(configuration.GetSection(AgentOptions.SectionName));

        if (!options.IsConfigured) return services;

        if (options.Anthropic.IsConfigured)
        {
            var apiKey = options.Anthropic.ApiKey;

            services.AddKeyedSingleton<IChatClient>(AiName.Sonnet_4_6, (_, _) =>
                new AnthropicClient(new Anthropic.Core.ClientOptions
                {
                    ApiKey = apiKey,
                    Timeout = ProviderTimeout
                })
                .AsIChatClient(AiName.Sonnet_4_6));
        }

        if (options.OpenAi.IsConfigured)
        {
            var apiKey = options.OpenAi.ApiKey;

            services.AddKeyedSingleton<IChatClient>(AiName.Gpt5_4_Mini, (_, _) =>
                new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), new OpenAIClientOptions
                {
                    NetworkTimeout = ProviderTimeout
                })
                .GetChatClient(AiName.Gpt5_4_Mini)
                .AsIChatClient());
        }

        // The default registration, under the bare key. Present only if its own model's provider is configured —
        // otherwise the variant below is the only one, and a request with no model override falls back to it through
        // AgentTurnService's second lookup.
        if (IsAvailable(options, SiteEditorAgent.Model))
            services.AddKeyedScoped<AIAgent>(SiteEditorAgent.Key, (sp, key) => SiteEditorAgent.Create(sp, (string)key!));

        // The cheap variant, addressable as site-editor-gpt-5.4-mini. Same agent class, same tools, different client —
        // which is the whole reason the key carries the model rather than a second agent existing.
        if (IsAvailable(options, AiName.Gpt5_4_Mini))
            services.AddKeyedScoped<AIAgent>($"{SiteEditorAgent.Key}-{AiName.Gpt5_4_Mini}",
                (sp, key) => SiteEditorAgent.Create(sp, (string)key!));

        return services;
    }

    private static bool IsAvailable(AgentOptions options, string model) => model switch
    {
        AiName.Sonnet_4_6 => options.Anthropic.IsConfigured,
        AiName.Gpt5_4_Mini => options.OpenAi.IsConfigured,
        _ => false
    };
}
