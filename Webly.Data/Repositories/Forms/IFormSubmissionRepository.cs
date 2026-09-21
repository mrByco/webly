using Webly.Data.Models.Forms;

namespace Webly.Data.Repositories.Forms;

public interface IFormSubmissionRepository
{
    /// <summary>This site's submissions, newest first, with their fields.</summary>
    Task<List<FormSubmission>> ListForSiteAsync(int siteId, int take, CancellationToken cancellationToken = default);

    Task<int> CountForSiteAsync(int siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many this site has taken since a moment. The only thing standing between an unauthenticated
    /// endpoint and somebody's disk, so it is a count over the <c>(SiteId, CreatedAt)</c> index rather
    /// than anything cleverer.
    /// </summary>
    Task<int> CountSinceAsync(int siteId, DateTime since, CancellationToken cancellationToken = default);

    void Add(FormSubmission submission);
}
