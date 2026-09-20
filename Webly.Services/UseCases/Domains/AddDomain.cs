using Webly.Data;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Domains;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Domains;
using Webly.Services.UseCases.Sites;

namespace Webly.Services.UseCases.Domains;

/// <summary>
/// Connects a custom hostname to a site: attach it at the provider, store what it says to put in DNS, and hand the
/// records to the person.
///
/// The provider is called <b>before</b> the row is written, and the row mirrors its answer. The other order would
/// give us a domain the provider has never heard of, which is a screen that shows DNS instructions nobody can act
/// on — and the reason <c>Domain.VerificationState</c> is never decided locally.
///
/// Attaching needs a provider-side project, so this creates one if the site has never been published. A domain
/// pointing at a project with nothing in it serves the provider's placeholder, which is honest: the person has not
/// published yet.
/// </summary>
public class AddDomain(
    ISiteRepository siteRepository,
    IDomainRepository domainRepository,
    IDeploymentTarget deploymentTarget,
    WeblyDbContext dbContext)
{
    public async Task<Result<DomainError, DomainResponse>> ExecuteAsync(
        int userId,
        string siteNanoid,
        AddDomainRequest request,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<DomainError, DomainResponse>.Fail(DomainError.SiteNotFound);

        if (Hostname.TryNormalize(request.Hostname) is not { } hostname)
            return Result<DomainError, DomainResponse>.Fail(DomainError.InvalidHostname);

        // One answer whether it is this site's or a stranger's: which of the two would tell somebody whether a
        // domain they do not own is hosted on Webly.
        if (await domainRepository.FindByHostnameAsync(hostname, cancellationToken) is not null)
            return Result<DomainError, DomainResponse>.Fail(DomainError.AlreadyConnected);

        if (site.ProviderProjectId is null)
        {
            site.ProviderProjectId = await deploymentTarget.EnsureProjectAsync(site.Nanoid, site.Name, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        DomainAttachment attachment;

        try
        {
            attachment = await deploymentTarget.AttachDomainAsync(site.ProviderProjectId, hostname, cancellationToken);
        }
        catch (DeploymentFailedException exception)
        {
            return Result<DomainError, DomainResponse>.Fail(DomainError.ProviderRefused, exception.Message);
        }

        var domain = new Domain
        {
            SiteId = site.Id,
            Hostname = hostname,
            VerificationState = attachment.Verified ? DomainVerificationState.Verified : DomainVerificationState.Pending,
            ProviderDomainId = attachment.ProviderDomainId,
            DnsRecordType = attachment.RecordType,
            DnsRecordName = attachment.RecordName,
            DnsRecordValue = attachment.RecordValue,
            VerifiedAt = attachment.Verified ? DateTime.UtcNow : null,
            LastError = attachment.Error,
            // Not primary on arrival, even if it verified instantly: promoting a domain changes every canonical URL
            // on the site, and that is a decision, not a side effect of adding one.
            IsPrimary = false
        };

        domainRepository.Add(domain);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<DomainError, DomainResponse>.Ok(SiteMapper.ToDomain(domain));
    }
}
