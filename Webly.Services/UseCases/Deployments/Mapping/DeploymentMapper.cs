using Webly.Data.Models.Deployments;
using Webly.Data.Models.Sites;
using Webly.Services.DTO.Deployments;

namespace Webly.Services.UseCases.Deployments.Mapping;

public static class DeploymentMapper
{
    public static DeploymentResponse ToResponse(Deployment deployment, SiteVersion version) => new()
    {
        Nanoid = deployment.Nanoid,
        Status = deployment.Status,
        SiteVersionNanoid = version.Nanoid,
        SiteVersionSummary = version.Summary,
        ProviderUrl = deployment.ProviderUrl,
        Error = deployment.Error,
        CreatedAt = deployment.CreatedAt,
        FinishedAt = deployment.FinishedAt
    };
}
