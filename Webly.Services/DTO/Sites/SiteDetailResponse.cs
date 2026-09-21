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
    /// The publish that is running right now, if one is, so a page that loads mid-publish can re-attach to it.
    ///
    /// A deployment's run id <i>is</i> its nanoid, deliberately, which is what makes a publish the one run in
    /// this product that outlives the process that started it — and nothing used that, because nothing on load
    /// knew a publish was in flight. Reload the editor while a site is publishing and the button read "Publish"
    /// again, offering to start what was already running; pressing it was safe (the partial unique index
    /// refuses a second, and `PublishSite` hands back the one that is going) but the screen was lying until
    /// somebody pressed. Same shape as `activeRunId` on the chat, for the same reason.
    ///
    /// One index seek, over the partial unique index that already enforces one live publish per site.
    /// </summary>
    public string? ActiveDeploymentNanoid { get; init; }

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
