using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using Webly.Services.Agent;
using Webly.Services.Agent.Agents;
using Webly.Services.Services.Sandboxes;

namespace Webly.Tests;

/// <summary>
/// Both agents are offered the browser tools, under one name, started by one command. Each CLI takes its MCP
/// configuration in its own shape — a flag for Claude Code, a key in the inline config for OpenCode — and a server
/// configured for one and not the other is an agent that cannot do what <c>AGENTS.md</c> tells it it can.
/// </summary>
public class BrowserToolsTests
{
    [Test]
    public async Task Claude_Code_starts_only_the_browser_server_and_may_use_its_tools()
    {
        var agent = new ClaudeCodeAgent(
            Options.Create(new CodingAgentOptions { ClaudeCode = new CodingAgentOptions.ClaudeCodeOptions { ApiKey = "sk-test" } }),
            NullLogger<ClaudeCodeAgent>.Instance);

        var sandbox = new ScriptedSandbox(
            exitCode: 0,
            """{"type":"result","subtype":"success","is_error":false,"result":"Nothing to change.\nSUMMARY: none"}""" + "\n");

        await agent.RunAsync(sandbox, new CodingAgentRequest("Look at the home page", [], null), _ => Task.CompletedTask);

        var arguments = sandbox.Command!.Arguments.ToList();
        var config = JsonNode.Parse(arguments[arguments.IndexOf("--mcp-config") + 1])!;

        Assert.Multiple(() =>
        {
            Assert.That(config["mcpServers"]!.AsObject().Select(server => server.Key), Is.EqualTo(new[] { "playwright" }));
            Assert.That(config["mcpServers"]!["playwright"]!["command"]!.GetValue<string>(), Is.EqualTo("webly-browser-mcp"));
            Assert.That(arguments, Does.Contain("--strict-mcp-config"), "and no server the site's own files declare");
            Assert.That(
                arguments[arguments.IndexOf("--allowedTools") + 1].Split(','),
                Does.Contain("mcp__playwright"),
                "a headless run refuses an MCP tool nobody pre-approved");
        });
    }

    [Test]
    public void OpenCode_starts_the_same_server_under_the_same_name()
    {
        var environment = OpenCodeAgent.EnvironmentFor(new CodingAgentOptions.OpenCodeOptions
        {
            ApiKey = "sk-test",
            Model = "openai/gpt-5.5"
        });

        var server = JsonNode.Parse(environment["OPENCODE_CONFIG_CONTENT"])!["mcp"]!["playwright"]!;

        Assert.Multiple(() =>
        {
            Assert.That(server["type"]!.GetValue<string>(), Is.EqualTo("local"));
            Assert.That(server["command"]!.AsArray().Select(part => part!.GetValue<string>()), Is.EqualTo(new[] { "webly-browser-mcp" }));
        });
    }

    /// <summary>
    /// The first real turn that had them used ten in a row — resize, open the page, click Menu, click Close menu —
    /// and the chat said "Working" for each, because neither parser knew the names.
    /// </summary>
    [Test]
    public async Task Both_chats_say_the_agent_is_trying_the_site()
    {
        var phrases = new List<string>();

        Task Collect(CodingAgentEvent @event)
        {
            if (@event is CodingAgentEvent.Activity activity) phrases.Add(activity.Phrase);
            return Task.CompletedTask;
        }

        await new ClaudeStreamJsonParser(Collect, NullLogger.Instance).HandleAsync(new SandboxOutput(false,
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"mcp__playwright__browser_click","input":{"element":"Menu button"}}]}}""" + "\n"));

        await new OpenCodeJsonParser("openai/gpt-5.5", Collect, NullLogger.Instance).HandleAsync(new SandboxOutput(false,
            """{"type":"tool_use","sessionID":"ses_1","part":{"tool":"playwright_browser_click","state":{"input":{"element":"Menu button"}}}}""" + "\n"));

        Assert.That(phrases, Is.EqualTo(new[] { "Trying your site in a browser", "Trying your site in a browser" }));
    }
}
