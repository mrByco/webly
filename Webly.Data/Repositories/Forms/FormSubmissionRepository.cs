using Microsoft.EntityFrameworkCore;
using Webly.Data.Models.Forms;

namespace Webly.Data.Repositories.Forms;

public class FormSubmissionRepository(WeblyDbContext dbContext) : IFormSubmissionRepository
{
    public Task<List<FormSubmission>> ListForSiteAsync(
        int siteId,
        int take,
        CancellationToken cancellationToken = default) =>
        dbContext.FormSubmissions
            .Where(x => x.SiteId == siteId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public Task<int> CountForSiteAsync(int siteId, CancellationToken cancellationToken = default) =>
        dbContext.FormSubmissions.CountAsync(x => x.SiteId == siteId, cancellationToken);

    public Task<int> CountSinceAsync(int siteId, DateTime since, CancellationToken cancellationToken = default) =>
        dbContext.FormSubmissions.CountAsync(
            x => x.SiteId == siteId && x.CreatedAt >= since, cancellationToken);

    public Task<int> CountUnreadAsync(int siteId, CancellationToken cancellationToken = default) =>
        dbContext.FormSubmissions.CountAsync(x => x.SiteId == siteId && x.ReadAt == null, cancellationToken);

    public Task<int> MarkReadAsync(int siteId, DateTime at, CancellationToken cancellationToken = default) =>
        dbContext.FormSubmissions
            .Where(x => x.SiteId == siteId && x.ReadAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.ReadAt, at), cancellationToken);

    public Task<FormSubmission?> FindForSiteAsync(
        int siteId,
        string nanoid,
        CancellationToken cancellationToken = default) =>
        dbContext.FormSubmissions
            .FirstOrDefaultAsync(x => x.SiteId == siteId && x.Nanoid == nanoid, cancellationToken);

    public void Add(FormSubmission submission) => dbContext.FormSubmissions.Add(submission);

    public void Remove(FormSubmission submission) => dbContext.FormSubmissions.Remove(submission);
}
