namespace Webly.Services.Agent.Agents;

/// <summary>
/// The browser both agents are offered, beside the <c>webly-screenshot</c> command: the Playwright MCP server,
/// which the CLI starts inside the sandbox through <c>webly-browser-mcp</c> (<c>tools/sandbox-agent</c>). The
/// command is for a quick look at a page at two widths; the tools are for a look that needs something done first —
/// opening the phone menu, unfolding a section, following a link. Both are offered and the agent decides, because
/// which one a change needs is a judgement about the change, and making it is the agent's job.
///
/// What it costs is its tool definitions — about five thousand tokens — on every model call. They sit at the
/// front of the prompt, which both providers cache, so a turn's later calls read them at the cached price.
///
/// <c>webly-browser-mcp</c> is where everything about the sandbox is decided: which browser, where its output
/// goes (never into the site), where the site is running, and the one tool withheld. This class only names it, so
/// that both agents start the same thing under the same name.
/// </summary>
internal static class BrowserTools
{
    /// <summary>
    /// <c>playwright</c>, because that is the name both models have met these tools under — Claude Code calls them
    /// <c>mcp__playwright__browser_navigate</c> and so on — and a familiar tool is one a model uses well.
    /// </summary>
    public const string ServerName = "playwright";

    /// <summary>On every sandbox command's PATH, beside <c>webly-screenshot</c>.</summary>
    public const string Command = "webly-browser-mcp";

    /// <summary>
    /// What the chat says while the agent uses one: clicking a menu or resizing the window is somebody trying the
    /// site, and "Working" — which is what an unknown tool reads as — told the owner nothing ten times in a row.
    /// </summary>
    public const string Activity = "Trying your site in a browser";

    /// <summary>Claude Code's name for a tool of this server: <c>mcp__playwright__browser_click</c>.</summary>
    public const string ClaudeToolPrefix = $"mcp__{ServerName}__";

    /// <summary>OpenCode's: <c>playwright_browser_click</c>, as its own store records it.</summary>
    public const string OpenCodeToolPrefix = $"{ServerName}_";
}
