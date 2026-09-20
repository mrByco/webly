using Webly.Data.Models.Sites;

namespace Webly.Data.Repositories.Domains;

public interface IDomainRepository
{
    Task<List<Domain>> ListForSiteAsync(int siteId, CancellationToken cancellationToken = default);

    Task<Domain?> FindForSiteAsync(string nanoid, int siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks a hostname up across every site. Used to answer "that domain is already connected to
    /// another Webly site" before the provider does, and by the renderer to resolve the canonical host.
    /// </summary>
    Task<Domain?> FindByHostnameAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>
    /// The hostname a site canonicalizes to, or null for one that has no verified custom domain and is
    /// therefore canonical at its <c>{slug}</c> subdomain.
    /// </summary>
    Task<Domain?> FindPrimaryAsync(int siteId, CancellationToken cancellationToken = default);

    void Add(Domain domain);

    void Remove(Domain domain);
}
