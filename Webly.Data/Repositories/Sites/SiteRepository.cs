using Microsoft.EntityFrameworkCore;
using Webly.Data.Models.Sites;

namespace Webly.Data.Repositories.Sites;

public class SiteRepository(WeblyDbContext dbContext) : ISiteRepository
{
    public Task<Site?> FindForOwnerAsync(string nanoid, int userId, CancellationToken cancellationToken = default) =>
        dbContext.Sites
            .Include(x => x.DraftVersion)
            .FirstOrDefaultAsync(x => x.Nanoid == nanoid && x.OwnerId == userId, cancellationToken);

    public Task<Site?> FindForOwnerLightAsync(string nanoid, int userId, CancellationToken cancellationToken = default) =>
        dbContext.Sites
            .FirstOrDefaultAsync(x => x.Nanoid == nanoid && x.OwnerId == userId, cancellationToken);

    public Task<List<Site>> ListForOwnerAsync(int userId, CancellationToken cancellationToken = default) =>
        dbContext.Sites
            .Where(x => x.OwnerId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);

    public Task<int> CountForOwnerAsync(int userId, CancellationToken cancellationToken = default) =>
        dbContext.Sites.CountAsync(x => x.OwnerId == userId, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) =>
        dbContext.Sites.AnyAsync(x => x.Slug == slug, cancellationToken);

    public void Add(Site site) => dbContext.Sites.Add(site);

    public void Remove(Site site) => dbContext.Sites.Remove(site);
}
