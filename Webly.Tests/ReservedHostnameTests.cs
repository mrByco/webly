using Webly.Services.Services;
using Webly.Services.Services.Deployments;

namespace Webly.Tests;

/// <summary>
/// The addresses Webly gives out, which nobody may connect to a site of their own. Every site is in one provider
/// account, so an attach of another site's <c>{slug}.{zone}</c> — or of a slug nobody has taken yet, or the zone
/// itself — would have been served at once.
/// </summary>
public class ReservedHostnameTests
{
    private static readonly SitesOptions Sites = new() { BaseDomain = "webly.site" };
    private static readonly AppOptions App = new() { BaseUrl = "https://app.webly.example/" };

    [TestCase("webly.site")]
    [TestCase("brightwater-florist.webly.site")]
    [TestCase("www.brightwater-florist.webly.site")]
    public void The_zone_and_everything_under_it_is_ours(string hostname) =>
        Assert.That(Sites.IsInZone(hostname), Is.True);

    // A label boundary, not a suffix: these are other people's domains that happen to end in the same letters.
    [TestCase("notwebly.site")]
    [TestCase("webly.site.example.com")]
    [TestCase("koopmancycles.nl")]
    public void A_domain_that_only_looks_like_it_is_not(string hostname) =>
        Assert.That(Sites.IsInZone(hostname), Is.False);

    [Test]
    public void The_zone_is_compared_as_configured_however_it_was_written() =>
        Assert.That(new SitesOptions { BaseDomain = " Webly.Site. " }.IsInZone("shop.webly.site"), Is.True);

    [TestCase("app.webly.example", true)]
    [TestCase("preview.app.webly.example", true)]
    [TestCase("webly.example", false)]
    [TestCase("myapp.webly.example", false)]
    public void The_apps_own_host_and_what_is_under_it_is_ours(string hostname, bool ours) =>
        Assert.That(App.IsOwnHost(hostname), Is.EqualTo(ours));
}
