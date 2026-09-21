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

    /// <summary>How many of this site's submissions the owner has not had on screen yet.</summary>
    Task<int> CountUnreadAsync(int siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stamps every unread submission of this site as read, and answers how many that was.
    ///
    /// One statement rather than loading and saving: the rows are not wanted, only the effect, and the screen
    /// that calls this has just been sent the same list it is acknowledging.
    /// </summary>
    Task<int> MarkReadAsync(int siteId, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>One of this site's submissions, or null. Never found by nanoid alone — see the interface's
    /// callers: a submission is reached through the site that owns it.</summary>
    Task<FormSubmission?> FindForSiteAsync(
        int siteId,
        string nanoid,
        CancellationToken cancellationToken = default);

    void Add(FormSubmission submission);

    void Remove(FormSubmission submission);
}
