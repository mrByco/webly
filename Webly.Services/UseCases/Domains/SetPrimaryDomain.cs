using Webly.Data;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Domains;

namespace Webly.Services.UseCases.Domains;

/// <summary>
/// Chooses the hostname the site canonicalizes to.
///
/// Only a verified domain may be promoted: a canonical URL on a hostname that does not resolve tells every search
/// engine that the real site is somewhere unreachable, which is worse than having no custom domain at all.
///
/// The old primary is demoted first and saved, because the partial unique index allows exactly one — writing both in
/// one batch would hit it. The index is what makes this safe against two people promoting different domains at once;
/// this ordering is what makes the ordinary case not fail against the index.
/// </summary>
public class SetPrimaryDomain(
    ISiteRepository siteRepository,
    IDomainRepository domainRepository,
    WeblyDbContext dbContext)
{
    public async Task<Result<DomainError>> ExecuteAsync(
        int userId,
        string siteNanoid,
        string domainNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<DomainError>.Fail(DomainError.SiteNotFound);

        var domain = await domainRepository.FindForSiteAsync(domainNanoid, site.Id, cancellationToken);

        if (domain is null) return Result<DomainError>.Fail(DomainError.DomainNotFound);

        if (domain.VerificationState is not DomainVerificationState.Verified)
            return Result<DomainError>.Fail(DomainError.NotVerified);

        if (await domainRepository.FindPrimaryAsync(site.Id, cancellationToken) is { } current)
        {
            if (current.Id == domain.Id) return Result<DomainError>.Ok();

            current.IsPrimary = false;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        domain.IsPrimary = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<DomainError>.Ok();
    }
}
