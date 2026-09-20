using Microsoft.EntityFrameworkCore;
using Webly.Data.Models.Sites;

namespace Webly.Data.Repositories.Domains;

public class DomainRepository(WeblyDbContext dbContext) : IDomainRepository
{
    public Task<List<Domain>> ListForSiteAsync(int siteId, CancellationToken cancellationToken = default) =>
        dbContext.Domains
            .Where(x => x.SiteId == siteId)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Hostname)
            .ToListAsync(cancellationToken);

    public Task<Domain?> FindForSiteAsync(string nanoid, int siteId, CancellationToken cancellationToken = default) =>
        dbContext.Domains
            .FirstOrDefaultAsync(x => x.Nanoid == nanoid && x.SiteId == siteId, cancellationToken);

    public Task<Domain?> FindByHostnameAsync(string hostname, CancellationToken cancellationToken = default) =>
        dbContext.Domains
            .FirstOrDefaultAsync(x => x.Hostname == hostname, cancellationToken);

    public Task<Domain?> FindPrimaryAsync(int siteId, CancellationToken cancellationToken = default) =>
        dbContext.Domains
            .FirstOrDefaultAsync(x => x.SiteId == siteId && x.IsPrimary, cancellationToken);

    public void Add(Domain domain) => dbContext.Domains.Add(domain);

    public void Remove(Domain domain) => dbContext.Domains.Remove(domain);
}
