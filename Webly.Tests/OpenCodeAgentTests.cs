using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using Webly.Services.Agent;
using Webly.Services.Agent.Agents;
using Webly.Services.Services.Sandboxes;

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

    /// <summary>
    /// A run that dies halfway has already printed prose, and that is what an OpenAI account running out of credit
    /// mid-turn looked like: the exit code said so and nothing else did, so three half-built sites were committed
    /// as successful turns under their owners' own messages, each with a reply that stopped mid-sentence.
    /// </summary>
    [Test]
    public void A_run_that_fails_after_saying_something_is_still_a_failure()
    {
        var agent = new OpenCodeAgent(
            Options.Create(new CodingAgentOptions
            {
                OpenCode = new CodingAgentOptions.OpenCodeOptions { ApiKey = "sk-test", Model = "openai/gpt-5.5" }
            }),
            NullLogger<OpenCodeAgent>.Instance);

        var sandbox = new ScriptedSandbox(
            exitCode: 1,
            "I'm applying the content and page changes now.\n");

        Assert.That(
            async () => await agent.RunAsync(sandbox, new CodingAgentRequest("Build a menu page", [], null), _ => Task.CompletedTask),
            Throws.InstanceOf<SandboxException>());
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
