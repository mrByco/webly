using Webly.Services.DTO.Domains;

namespace Webly.Services.DTO.Sites;

/// <summary>One site, with what the editor's header and panes need on first load.</summary>
public record SiteDetailResponse
{
    public required SiteSummaryResponse Summary { get; init; }

    /// <summary>The tip of the branch: what the chat is editing and the preview is showing.</summary>
    public required SiteVersionResponse HeadVersion { get; init; }

    public SiteVersionResponse? PublishedVersion { get; init; }

    public IReadOnlyList<DomainResponse> Domains { get; init; } = [];

    /// <summary>
    /// Whether a workspace is warm for this site. The editor uses it to say "waking up your site" before the
    /// first message rather than after it — the one moment this product makes somebody wait.
    /// </summary>
    public required bool WorkspaceReady { get; init; }
}
