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
            // The document is the one column a history list never needs, and the only large one.
            // Projected away rather than trusted to laziness: the property is non-nullable, so the
            // projection builds the entity with an empty document and the entities are untracked.
            .AsNoTracking()
            .Select(x => new SiteVersion
            {
                Id = x.Id,
                Nanoid = x.Nanoid,
                CreatedAt = x.CreatedAt,
                SiteId = x.SiteId,
                ParentVersionId = x.ParentVersionId,
                Summary = x.Summary,
                Origin = x.Origin,
                CreatedByUserId = x.CreatedByUserId,
                SourceMessageId = x.SourceMessageId,
                RestoredFromVersionId = x.RestoredFromVersionId,
                Document = new()
            })
            .ToListAsync(cancellationToken);

    public Task<int> CountForSiteAsync(int siteId, CancellationToken cancellationToken = default) =>
        dbContext.SiteVersions.CountAsync(x => x.SiteId == siteId, cancellationToken);

    public void Add(SiteVersion version) => dbContext.SiteVersions.Add(version);
}
