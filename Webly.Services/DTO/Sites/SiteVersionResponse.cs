using Webly.Data.Models.Sites;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.DTO.Sites;

/// <summary>
/// One entry of a site's history. <see cref="Document"/> is null in a list and present when a single version is
/// asked for — the same record either way rather than two near-identical ones, because a list row and a detail
/// differ here only by whether the expensive field was loaded.
/// </summary>
public record SiteVersionResponse
{
    public required string Nanoid { get; init; }
    public required string Summary { get; init; }
    public required SiteVersionOrigin Origin { get; init; }
    public required DateTime CreatedAt { get; init; }

    /// <summary>The version this one restored, when it was a restore.</summary>
    public string? RestoredFromNanoid { get; init; }

    /// <summary>Whether this is the version currently being edited.</summary>
    public required bool IsDraft { get; init; }

    /// <summary>Whether this is the version currently live.</summary>
    public required bool IsPublished { get; init; }

    public SiteDocument? Document { get; init; }
}
