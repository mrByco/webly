namespace Webly.Data.Models.Usage;

public enum UsageKind
{
    /// <summary>The model calls of one agent turn.</summary>
    AgentTurn,

    /// <summary>A sandbox kept warm for editing: the agent, the dev server and the preview.</summary>
    EditingSandbox,

    /// <summary>A fresh sandbox that built a publish.</summary>
    PublishSandbox
}
