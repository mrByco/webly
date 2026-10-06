using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Webly.Api.Extensions;
using Webly.Services.Agent;
using Webly.Services.Agent.Agents;

namespace Webly.Tests;

/// <summary>
/// Which credential Claude Code runs on. An API key or a developer's own subscription token, and exactly one of them
/// reaching the CLI — the other set empty, because an unconfined local sandbox inherits the developer's environment,
/// where an exported key would otherwise quietly win. The CLI says nothing about which it used, so nothing but a test
/// keeps it right.
/// </summary>
public class ClaudeCodeAgentTests
{
    [Test]
    public void A_subscription_token_alone_runs_the_agent_on_the_subscription()
    {
        var options = new CodingAgentOptions.ClaudeCodeOptions { OAuthToken = "sk-ant-oat-test" };
        var environment = ClaudeCodeAgent.EnvironmentFor(options);

        Assert.Multiple(() =>
        {
            Assert.That(options.IsConfigured, Is.True, "a token is enough for the agent to be offered");
            Assert.That(environment["CLAUDE_CODE_OAUTH_TOKEN"], Is.EqualTo("sk-ant-oat-test"));
            Assert.That(environment["ANTHROPIC_API_KEY"], Is.Empty, "and an inherited key cannot take over");
        });
    }

    [Test]
    public void An_api_key_wins_over_a_subscription_token()
    {
        var environment = ClaudeCodeAgent.EnvironmentFor(new CodingAgentOptions.ClaudeCodeOptions
        {
            ApiKey = "sk-ant-api-test",
            OAuthToken = "sk-ant-oat-test"
        });

        Assert.Multiple(() =>
        {
            Assert.That(environment["ANTHROPIC_API_KEY"], Is.EqualTo("sk-ant-api-test"));
            Assert.That(environment["CLAUDE_CODE_OAUTH_TOKEN"], Is.Empty, "so adding a real key retires the subscription");
        });
    }

    [Test]
    public void Neither_means_the_agent_is_absent()
    {
        Assert.That(new CodingAgentOptions.ClaudeCodeOptions().IsConfigured, Is.False);
    }

    /// <summary>A subscription is one person's: anywhere but Development the app refuses to boot with one.</summary>
    [TestCase("Production", true)]
    [TestCase("Staging", true)]
    [TestCase("Development", false)]
    public void A_subscription_token_is_refused_outside_development(string environmentName, bool refused)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:ClaudeCode:OAuthToken"] = "sk-ant-oat-test",
                ["Sandbox:Provider"] = "docker",
                ["Deployment:Provider"] = "vercel"
            })
            .Build();

        var environment = new HostingEnvironment { EnvironmentName = environmentName };

        var register = () => new ServiceCollection().AddWeblySites(configuration, environment);

        if (refused)
            Assert.That(register, Throws.InvalidOperationException.With.Message.Contains("Agent:ClaudeCode:OAuthToken"));
        else
            Assert.That(register, Throws.Nothing);
    }
}
