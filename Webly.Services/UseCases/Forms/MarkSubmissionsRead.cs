using Webly.Data.Repositories.Forms;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;

namespace Webly.Services.UseCases.Forms;

/// <summary>
/// Acknowledges everything a site has taken: the owner has had the list in front of them.
///
/// <b>Everything unread, not a list of ids</b>, and the reason is what the screen does. It shows every message
/// in full, newest first, with no preview to click through — so there is no state in which somebody has seen
/// one message and not the one under it, and an endpoint taking ids would be a more precise answer to a
/// question nobody asked, paid for in a request body that grows with the inbox.
///
/// It is deliberately not folded into the list endpoint. A <c>GET</c> that writes is a <c>GET</c> that a
/// prefetch, a bot or a second tab can spend, and the one thing this product must not do is make somebody's
/// unread messages disappear because something loaded a page they never looked at.
/// </summary>
public class MarkSubmissionsRead(
    ISiteRepository siteRepository,
    IFormSubmissionRepository submissions)
{
    public async Task<Result<FormError>> ExecuteAsync(
        int userId,
        string siteNanoid,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerLightAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<FormError>.Fail(FormError.SiteNotFound);

        await submissions.MarkReadAsync(site.Id, DateTime.UtcNow, cancellationToken);

        return Result<FormError>.Ok();
    }
}
