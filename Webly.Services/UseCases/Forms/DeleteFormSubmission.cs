using Webly.Data;
using Webly.Data.Repositories.Forms;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;

namespace Webly.Services.UseCases.Forms;

/// <summary>
/// Throws one message away.
///
/// <b>It really goes.</b> There is no archive, no trash and no soft-deleted flag, and that is a decision
/// rather than an omission: a message is a few hundred bytes somebody sent, the reason to remove one is that
/// it is spam or that it has been dealt with, and a second list nobody empties is somewhere for the same
/// messages to accumulate out of sight. The owner has been emailed a copy of every one of these, which is the
/// backup — and it is theirs, not ours.
///
/// A message that has not been read yet can be deleted like any other. Somebody clearing five identical bits
/// of spam should not have to read them first.
/// </summary>
public class DeleteFormSubmission(
    ISiteRepository siteRepository,
    IFormSubmissionRepository submissions,
    WeblyDbContext dbContext)
{
    public async Task<Result<FormError>> ExecuteAsync(
        int userId,
        string siteNanoid,
        string submissionNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<FormError>.Fail(FormError.SiteNotFound);

        // Found through the site, never by its own nanoid: that is what stops one account's submission id
        // reaching another account's list, and it is the same shape every per-site lookup here has.
        var submission = await submissions.FindForSiteAsync(site.Id, submissionNanoid, cancellationToken);

        // Already gone reads as gone. A second click on a delete button, or a reload of a stale list, is not
        // something to report as an error.
        if (submission is null) return Result<FormError>.Ok();

        submissions.Remove(submission);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<FormError>.Ok();
    }
}
