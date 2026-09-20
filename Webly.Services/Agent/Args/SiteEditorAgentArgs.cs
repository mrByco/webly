using Microsoft.Extensions.DependencyInjection;
using Webly.Services.Agent.Tools;

namespace Webly.Services.Agent.Args;

/// <summary>The only agent Webly has: the one that edits the site you are looking at.</summary>
public class SiteEditorAgentArgs : BaseAgentArgs
{
    /// <summary>Which site. Authorized by the hub before the run starts, never trusted from here on.</summary>
    public required string SiteNanoid { get; set; }

    public override void SetupScope(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<AgentRunContext>().SiteNanoid = SiteNanoid;
}
