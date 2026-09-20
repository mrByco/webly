using Webly.Data.Models.Deployments;

namespace Webly.Services.DTO.Deployments;

public record DeploymentResponse
{
    public required string Nanoid { get; init; }
    public required DeploymentStatus Status { get; init; }

    /// <summary>Which version this put live, so the history can mark it.</summary>
    public required string SiteVersionNanoid { get; init; }
    public required string SiteVersionSummary { get; init; }

    /// <summary>The provider's immutable URL for this exact deployment — how an old version stays previewable.</summary>
    public string? ProviderUrl { get; init; }

    public string? Error { get; init; }

    /// <summary>The build log's tail, for the disclosure under a failed deployment.</summary>
    public string? ErrorDetail { get; init; }

    public required DateTime CreatedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
}
