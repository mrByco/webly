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

    /// <summary>
    /// How many messages the owner has not had on screen yet.
    ///
    /// On the site rather than fetched by the Messages screen, because the whole point of it is to be visible
    /// from the screens that are <i>not</i> Messages: somebody who never opens that tab has no way of knowing
    /// an enquiry is sitting in it. It is a count over a partial index of the unread rows, so the usual answer
    /// — zero — costs almost nothing.
    /// </summary>
    public required int UnreadSubmissionCount { get; init; }
}
