using Microsoft.Extensions.Options;

namespace Webly.Services.Agent;

/// <summary>
/// Which agent serves a turn. One lookup, so nothing else has to know that there is more than one — and so
/// "the configured default is not configured" is answered in one place rather than at every call site.
/// </summary>
public class CodingAgentRegistry(IEnumerable<ICodingAgent> agents, IOptions<CodingAgentOptions> options)
{
    private readonly Dictionary<string, ICodingAgent> _agents =
        agents.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The agents this deployment can actually run, for <c>/chat/status</c> and the client's picker.</summary>
    public IReadOnlyList<string> Available => [.. _agents.Values.Where(x => x.IsConfigured).Select(x => x.Key)];

    public bool AnyConfigured => Available.Count > 0;

    /// <summary>
    /// The agent for a request. An unknown or unconfigured name falls back to whatever is configured rather
    /// than failing: the person asked for a change to their website, not for a particular agent, and the
    /// difference is not something the product exposes.
    /// </summary>
    public ICodingAgent Resolve(string? requested)
    {
        if (requested is { Length: > 0 }
            && _agents.TryGetValue(requested, out var named)
            && named.IsConfigured)
            return named;

        if (_agents.TryGetValue(options.Value.Default, out var configured) && configured.IsConfigured)
            return configured;

        return _agents.Values.FirstOrDefault(x => x.IsConfigured)
            ?? throw new InvalidOperationException("No coding agent is configured in this environment.");
    }
}
