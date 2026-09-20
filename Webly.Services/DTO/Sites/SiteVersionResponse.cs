using Webly.Data.Models.Sites;

namespace Webly.Services.DTO.Sites;

/// <summary>
/// One entry of a site's history — which is one commit. No file contents: the history list draws from this,
/// and a client that wants what changed asks for the diff or reads a file.
/// </summary>
public record SiteVersionResponse
{
    public required string Nanoid { get; init; }

    /// <summary>
    /// The commit. Shown short in the UI, because a site's owner may not know what a commit is but every
    /// developer who ever exports this repository will — and the two views should agree about what a version is.
    /// </summary>
    public required string CommitSha { get; init; }

    public required string Summary { get; init; }

    /// <summary>The agent's longer account of the change, when it wrote one.</summary>
    public string? Details { get; init; }

    public required SiteVersionOrigin Origin { get; init; }

    public required int ChangedFileCount { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>The version this one restored, when it was a restore.</summary>
    public string? RestoredFromNanoid { get; init; }

    /// <summary>Whether this is the tip of the branch — what the editor is changing.</summary>
    public required bool IsHead { get; init; }

    /// <summary>Whether this is the version currently live.</summary>
    public required bool IsPublished { get; init; }
}
