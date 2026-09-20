using Webly.Data;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Domains;
using Webly.Services.Services.Deployments;
using Webly.Services.UseCases.Sites;

namespace Webly.Services.UseCases.Domains;

/// <summary>
/// Asks the provider whether a hostname resolves here yet, and mirrors the answer.
///
/// Driven by the person clicking "check again" rather than by a timer. A poll per pending domain per minute is a
/// provider rate limit waiting to happen, and the person watching the screen while their registrar's panel is open
/// in the next tab is a better trigger than a clock — they know when they changed something.
/// </summary>
public class CheckDomain(
    ISiteRepository siteRepository,
    IDomainRepository domainRepository,
    IDeploymentTarget deploymentTarget,
    WeblyDbContext dbContext)
{
    public async Task<Result<DomainError, DomainResponse>> ExecuteAsync(
        int userId,
        string siteNanoid,
        string domainNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<DomainError, DomainResponse>.Fail(DomainError.SiteNotFound);

        var domain = await domainRepository.FindForSiteAsync(domainNanoid, site.Id, cancellationToken);

        if (domain is null || site.ProviderProjectId is null)
            return Result<DomainError, DomainResponse>.Fail(DomainError.DomainNotFound);

        DomainAttachment attachment;

        try
        {
            attachment = await deploymentTarget.CheckDomainAsync(site.ProviderProjectId, domain.Hostname, cancellationToken);
        }
        catch (DeploymentFailedException exception)
        {
            return Result<DomainError, DomainResponse>.Fail(DomainError.ProviderRefused, exception.Message);
        }

        domain.VerificationState = attachment.Verified
            ? DomainVerificationState.Verified
            // Back to Pending rather than Failed when the provider simply has not seen the records yet. Failed means
            // "there is something to fix", and thirty seconds after an edit at a registrar there usually is not.
            : attachment.Error is null ? DomainVerificationState.Pending : DomainVerificationState.Failed;

        domain.DnsRecordType = attachment.RecordType ?? domain.DnsRecordType;
        domain.DnsRecordName = attachment.RecordName ?? domain.DnsRecordName;
        domain.DnsRecordValue = attachment.RecordValue ?? domain.DnsRecordValue;
        domain.LastError = attachment.Error;

        if (attachment.Verified) domain.VerifiedAt ??= DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<DomainError, DomainResponse>.Ok(SiteMapper.ToDomain(domain));
    }
}
