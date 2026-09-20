using Microsoft.EntityFrameworkCore;
using Webly.Data.Models.Sites;

namespace Webly.Data.Repositories.Sites;

public class SiteVersionRepository(WeblyDbContext dbContext) : ISiteVersionRepository
{
    public Task<SiteVersion?> FindForSiteAsync(string nanoid, int siteId, CancellationToken cancellationToken = default) =>
        dbContext.SiteVersions
            .FirstOrDefaultAsync(x => x.Nanoid == nanoid && x.SiteId == siteId, cancellationToken);

    public Task<SiteVersion?> FindByIdForSiteAsync(int id, int siteId, CancellationToken cancellationToken = default) =>
        dbContext.SiteVersions
            .FirstOrDefaultAsync(x => x.Id == id && x.SiteId == siteId, cancellationToken);

    public Task<List<SiteVersion>> ListForSiteAsync(int siteId, int skip, int take, CancellationToken cancellationToken = default) =>
        dbContext.SiteVersions
            .Where(x => x.SiteId == siteId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            // Untracked: a history list is read and drawn, never edited, and the change tracker would
            // hold every page anybody scrolled through for the life of the request. There is nothing to
            // project away — this row is the index into the repository, so its largest column is a
            // sentence; the file contents live in git.
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public Task<int> CountForSiteAsync(int siteId, CancellationToken cancellationToken = default) =>
        dbContext.SiteVersions.CountAsync(x => x.SiteId == siteId, cancellationToken);

    public void Add(SiteVersion version) => dbContext.SiteVersions.Add(version);
}
