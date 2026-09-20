using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Domains;
using Webly.Services.UseCases.Sites;

namespace Webly.Services.UseCases.Domains;

public class ListDomains(ISiteRepository siteRepository, IDomainRepository domainRepository)
{
    public async Task<Result<DomainError, IReadOnlyList<DomainResponse>>> ExecuteAsync(
        int userId,
        string siteNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<DomainError, IReadOnlyList<DomainResponse>>.Fail(DomainError.SiteNotFound);

        var domains = await domainRepository.ListForSiteAsync(site.Id, cancellationToken);

        return Result<DomainError, IReadOnlyList<DomainResponse>>.Ok([.. domains.Select(SiteMapper.ToDomain)]);
    }
}
