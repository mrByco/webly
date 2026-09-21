using Microsoft.Extensions.Options;
using Webly.Data.Models.Deployments;
using Webly.Data.Models.Sites;
using Webly.Services.Services.Deployments;
using Webly.Services.UseCases.Sites;

namespace Webly.Tests;

/// <summary>
/// The two URLs a site has, and the difference between them. A site's <b>address</b> is what it is called; its
/// <b>live URL</b> is what somebody can open. They are the same thing only once the provider has been told to
/// serve that hostname, and the whole reason <c>Site.AddressReadyAt</c> exists is that until then the address is
/// somewhere the site is not — which is a link to a 404 in the header of a screen that just said "published".
/// </summary>
public class SiteUrlTests
{
    private static SiteMapper Mapper() =>
        new(Options.Create(new SitesOptions { BaseDomain = "webly.site" }));

    private static Site Site(int? publishedVersionId = 7, DateTime? addressReadyAt = null) => new()
    {
        Id = 1,
        Nanoid = "site-nanoid",
        Name = "Koopman Cycles",
        Slug = "koopman-cycles",
        PublishedVersionId = publishedVersionId,
        HeadVersionId = publishedVersionId,
        AddressReadyAt = addressReadyAt
    };

    private static Deployment Live() => new()
    {
        Id = 3,
        SiteId = 1,
        SiteVersionId = 7,
        Status = DeploymentStatus.Ready,
        ProviderUrl = "https://koopman-abc123.vercel.app/"
    };

    [Test]
    public void A_site_that_has_never_been_published_has_an_address_but_nothing_to_open()
    {
        var site = Site(publishedVersionId: null);
        var summary = Mapper().ToSummary(site, primaryDomain: null, publishedAt: null);

        Assert.That(summary.Url, Is.EqualTo("https://koopman-cycles.webly.site"));
        Assert.That(summary.LiveUrl, Is.Null);
    }

    [Test]
    public void A_published_site_whose_address_is_not_serving_yet_is_opened_at_its_deployment()
    {
        var summary = Mapper().ToSummary(Site(), primaryDomain: null, DateTime.UtcNow, Live());

        Assert.That(summary.Url, Is.EqualTo("https://koopman-cycles.webly.site"), "the address does not move");
        Assert.That(summary.LiveUrl, Is.EqualTo("https://koopman-abc123.vercel.app/"));
    }

    [Test]
    public void Once_the_provider_serves_the_subdomain_the_address_is_the_live_url()
    {
        var site = Site(addressReadyAt: DateTime.UtcNow);
        var summary = Mapper().ToSummary(site, primaryDomain: null, DateTime.UtcNow, Live());

        Assert.That(summary.LiveUrl, Is.EqualTo(summary.Url));
    }

    /// <summary>
    /// A verified custom domain wins over both, and needs no <c>AddressReadyAt</c>: verifying it is what attaching
    /// it to the provider means, so it is being served by the time the row says verified.
    /// </summary>
    [Test]
    public void A_verified_primary_domain_is_where_the_site_is()
    {
        var domain = new Domain
        {
            Hostname = "koopmancycles.nl",
            IsPrimary = true,
            VerificationState = DomainVerificationState.Verified
        };

        var summary = Mapper().ToSummary(Site(), domain, DateTime.UtcNow, Live());

        Assert.That(summary.Url, Is.EqualTo("https://koopmancycles.nl"));
        Assert.That(summary.LiveUrl, Is.EqualTo("https://koopmancycles.nl"));
    }

    /// <summary>
    /// A pending one does not, which is the mistake this whole pair of properties exists to make unwritable: a
    /// hostname somebody typed into the domains screen thirty seconds ago resolves nowhere.
    /// </summary>
    [Test]
    public void A_pending_primary_domain_is_not()
    {
        var domain = new Domain { Hostname = "koopmancycles.nl", IsPrimary = true };
        var summary = Mapper().ToSummary(Site(), domain, DateTime.UtcNow, Live());

        Assert.That(summary.Url, Is.EqualTo("https://koopman-cycles.webly.site"));
        Assert.That(summary.LiveUrl, Is.EqualTo("https://koopman-abc123.vercel.app/"));
    }
}
