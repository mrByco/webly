namespace Webly.Services.DTO.Sites;

/// <summary>A site in a list: enough for a card, no document.</summary>
public record SiteSummaryResponse
{
    public required string Nanoid { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }

    /// <summary>Where it is reachable — its custom primary domain if it has one, otherwise its Webly subdomain.</summary>
    public required string Url { get; init; }

    /// <summary>Null for a site that has never been published. What the "Live"/"Draft" badge reads.</summary>
    public DateTime? PublishedAt { get; init; }

    /// <summary>
    /// Whether the draft has moved on since the last publish. Computed from the two pointers rather than stored —
    /// a boolean beside them is a third fact that can disagree with both.
    /// </summary>
    public required bool HasUnpublishedChanges { get; init; }

    public required DateTime UpdatedAt { get; init; }
}
