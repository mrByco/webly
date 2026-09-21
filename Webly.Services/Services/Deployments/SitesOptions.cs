using System.ComponentModel.DataAnnotations;

namespace Webly.Services.Services.Deployments;

/// <summary>Where published sites live.</summary>
public class SitesOptions
{
    public const string SectionName = "Sites";

    /// <summary>
    /// The zone every site gets a subdomain of: <c>{slug}.{BaseDomain}</c>. Required, because a site with no address
    /// cannot be published and the publishing is the product — but it has a default in <c>appsettings.json</c>, so the
    /// only way to be without one is to delete it.
    /// </summary>
    [Required]
    public string BaseDomain { get; set; } = string.Empty;

    /// <summary>How many sites one account may own before the app says no. A plan limit in the crudest
    /// possible form; billing replaces it (MASTER_PLAN.md P7) rather than removes it.</summary>
    public int MaxSitesPerUser { get; set; } = 3;

    /// <summary>The hostname a site is given: the one the provider has to be told about before it resolves.</summary>
    public string HostFor(string slug) => $"{slug}.{BaseDomain}";

    /// <summary>The URL a site is reachable at on its Webly subdomain, once that subdomain is arranged.</summary>
    public string UrlFor(string slug) => $"https://{HostFor(slug)}";
}
