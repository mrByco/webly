using Webly.Data.Models.Sites;

namespace Webly.Data.Repositories.Sites;

public interface ISiteVersionRepository
{
    /// <summary>
    /// One version of a site the caller owns, document included. Takes the site id rather than
    /// checking ownership itself: the caller already holds an authorised <see cref="Site"/> from
    /// <see cref="ISiteRepository.FindForOwnerAsync"/>, and a second, differently-worded check is how
    /// two places end up disagreeing.
    /// </summary>
    Task<SiteVersion?> FindForSiteAsync(string nanoid, int siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same, by internal id, for the callers that hold one of the site's own pointers rather than a nanoid.
    /// Still takes the site id: a pointer read off a site row cannot name another site's version, and requiring it
    /// means no query in this repository can be written without the scope.
    /// </summary>
    Task<SiteVersion?> FindByIdForSiteAsync(int id, int siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The history, newest first, <b>without</b> documents — a history list that loaded every snapshot
    /// would transfer the whole site once per row.
    /// </summary>
    Task<List<SiteVersion>> ListForSiteAsync(int siteId, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountForSiteAsync(int siteId, CancellationToken cancellationToken = default);

    void Add(SiteVersion version);
}
