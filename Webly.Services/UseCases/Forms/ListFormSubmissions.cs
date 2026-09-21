using Webly.Data.Repositories.Forms;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Forms;

namespace Webly.Services.UseCases.Forms;

/// <summary>
/// What visitors have sent, for the site's owner.
///
/// Newest first and capped rather than paged: a contact form on a small business's site produces a list
/// somebody reads, not a dataset they query. When one of them needs paging it is a skip and a take here and a
/// button there, and guessing at the shape now would be a parameter nothing sends.
/// </summary>
public class ListFormSubmissions(ISiteRepository siteRepository, IFormSubmissionRepository submissions)
{
    public const int Take = 200;

    public async Task<Result<FormError, IReadOnlyList<FormSubmissionResponse>>> ExecuteAsync(
        int userId,
        string siteNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null)
            return Result<FormError, IReadOnlyList<FormSubmissionResponse>>.Fail(FormError.SiteNotFound);

        var rows = await submissions.ListForSiteAsync(site.Id, Take, cancellationToken);

        return Result<FormError, IReadOnlyList<FormSubmissionResponse>>.Ok(
        [
            .. rows.Select(x => new FormSubmissionResponse
            {
                Nanoid = x.Nanoid,
                FormName = x.FormName,
                CreatedAt = x.CreatedAt,
                Fields = [.. x.Fields.Select(f => new FormFieldResponse { Name = f.Name, Value = f.Value })]
            })
        ]);
    }
}
