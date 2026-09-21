namespace Webly.Services.DTO.Sites;

/// <summary>A site in a list: enough for a card, no document.</summary>
public record SiteSummaryResponse
{
    public required string Nanoid { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }

    /// <summary>
    /// The site's address — its custom primary domain if it has a verified one, otherwise its Webly subdomain.
    /// What it is called, not necessarily what serves it yet: see <see cref="LiveUrl"/>.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Where the published version can actually be opened, or null for a site that has never been published. Equal
    /// to <see cref="Url"/> once the provider serves that hostname, and the deployment's own URL until it does —
    /// so this is the one to link and <see cref="Url"/> the one to print.
    /// </summary>
    public string? LiveUrl { get; init; }

    /// <summary>Null for a site that has never been published. What the "Live"/"Draft" badge reads.</summary>
    public DateTime? PublishedAt { get; init; }

    /// <summary>
    /// Whether the draft has moved on since the last publish. Computed from the two pointers rather than stored —
    /// a boolean beside them is a third fact that can disagree with both.
    /// </summary>
    public required bool HasUnpublishedChanges { get; init; }

    public required DateTime UpdatedAt { get; init; }
}
