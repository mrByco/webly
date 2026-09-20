using Webly.Services.DTO.Domains;

namespace Webly.Services.DTO.Sites;

/// <summary>One site, with what the editor's header and sidebar need on first load.</summary>
public record SiteDetailResponse
{
    public required SiteSummaryResponse Summary { get; init; }

    /// <summary>The draft, which is what the editor shows and the agent edits.</summary>
    public required SiteVersionResponse DraftVersion { get; init; }

    public SiteVersionResponse? PublishedVersion { get; init; }

    public IReadOnlyList<DomainResponse> Domains { get; init; } = [];
}
