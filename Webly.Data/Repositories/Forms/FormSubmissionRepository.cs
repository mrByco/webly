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

    public void Add(FormSubmission submission) => dbContext.FormSubmissions.Add(submission);
}
