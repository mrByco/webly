using System.Text.Json.Nodes;
using Webly.Services.Agent;
using Webly.Services.Agent.Agents;

namespace Webly.Tests;

/// <summary>
/// Where OpenCode's key goes. The first run of this agent against the real CLI — an OpenAI key, an
/// <c>openai/…</c> model — exited 1 with "Unexpected server error", because the key was always handed over as
/// <c>ANTHROPIC_API_KEY</c>. The CLI says nothing about a key, so nothing but a test keeps it right.
/// </summary>
public class OpenCodeAgentTests
{
    [Test]
    public void The_key_goes_to_the_provider_the_model_names()
    {
        var environment = OpenCodeAgent.EnvironmentFor(new CodingAgentOptions.OpenCodeOptions
        {
            ApiKey = "sk-test",
            Model = "openai/gpt-5.5"
        });

        var config = JsonNode.Parse(environment["OPENCODE_CONFIG_CONTENT"])!;

        Assert.Multiple(() =>
        {
            Assert.That(config["provider"]!["openai"]!["options"]!["apiKey"]!.GetValue<string>(), Is.EqualTo("sk-test"));
            Assert.That(config["provider"]!.AsObject().Count, Is.EqualTo(1), "and to no other provider");
            Assert.That(environment.Keys, Has.None.EndsWith("_API_KEY"), "and not under any one provider's own name");
        });
    }

    [Test]
    public void A_model_that_names_no_provider_is_refused_with_a_sentence()
    {
        var options = new CodingAgentOptions.OpenCodeOptions { ApiKey = "sk-test", Model = "gpt-5.5" };

        Assert.That(
            () => OpenCodeAgent.EnvironmentFor(options),
            Throws.InvalidOperationException.With.Message.Contains("Agent:OpenCode:Model"));
    }
}
