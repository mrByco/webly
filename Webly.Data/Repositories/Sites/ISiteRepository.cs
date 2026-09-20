using Webly.Data.Models.Sites;

namespace Webly.Data.Repositories.Sites;

public interface ISiteRepository
{
    /// <summary>
    /// The site, but only if this user owns it. <b>The one place the ownership check lives</b>: every
    /// use case starts from what this returns, so none of them can express the check wrongly or forget
    /// it. A caller that gets null answers 404 — never 403, which would confirm that a guessed nanoid
    /// names a real site.
    ///
    /// Includes the draft version, because almost everything a caller does next needs the document.
    /// </summary>
    Task<Site?> FindForOwnerAsync(string nanoid, int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same check, without loading either version's document. For the operations that only touch the
    /// site row — rename, delete, domains, the deployment list — where dragging two jsonb documents
    /// along would be the most expensive part of the request.
    /// </summary>
    Task<Site?> FindForOwnerLightAsync(string nanoid, int userId, CancellationToken cancellationToken = default);

    /// <summary>The user's sites, newest first, without documents.</summary>
    Task<List<Site>> ListForOwnerAsync(int userId, CancellationToken cancellationToken = default);

    Task<int> CountForOwnerAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Whether a slug is already taken. Checked before insert so the person gets a sentence
    /// rather than a unique-violation, with the index behind it as the real guarantee.</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    void Add(Site site);

    void Remove(Site site);
}
