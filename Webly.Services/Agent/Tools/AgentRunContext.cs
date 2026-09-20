namespace Webly.Services.Agent.Tools;

/// <summary>
/// Who the agent is acting for and on what, for the length of one turn. Scoped, and populated by
/// <c>BaseAgentArgs.SetupScope</c> plus the launcher before any tool runs.
///
/// This is the whole of Webly's answer to "identity inside a run". There is no <c>HttpContext</c> in a run — the
/// run outlives the request that started it by design — so the caller is resolved once, on the hub, and lives
/// here. A toolkit reads <see cref="UserId"/> and passes it to a use case exactly as a controller would, which
/// is what keeps the agent on the same authorization path as the REST API instead of beside it.
/// </summary>
public class AgentRunContext
{
    public int UserId { get; set; }

    public string SiteNanoid { get; set; } = string.Empty;

    /// <summary>The run, so a tool that has to emit an event or wait for an answer can find its own.</summary>
    public string RunId { get; set; } = string.Empty;
}
