using Webly.Services.Services.Domains;
using Webly.Services.Services.Sites;

namespace Webly.Tests;

public class SiteSlugTests
{
    [Test]
    public void Accents_are_folded_rather_than_dropped()
    {
        Assert.That(SiteSlug.From("Kovács Bicikli"), Is.EqualTo("kovacs-bicikli"));
    }

    [Test]
    public void Punctuation_becomes_one_hyphen_and_never_leads_or_trails()
    {
        Assert.That(SiteSlug.From("  Anna's  Café & Bar!  "), Is.EqualTo("anna-s-cafe-bar"));
    }

    /// <summary>
    /// A reserved label would shadow the platform's own hostnames, and an empty one cannot be published
    /// at all. Both fall back to something random rather than interrupting a signup.
    /// </summary>
    [Test]
    public void A_reserved_or_unusable_name_falls_back_to_a_random_slug()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SiteSlug.From("www"), Does.StartWith("site-"));
            Assert.That(SiteSlug.From("!!!"), Does.StartWith("site-"));
        });
    }

    [Test]
    public void A_slug_never_exceeds_a_dns_label()
    {
        Assert.That(SiteSlug.From(new string('a', 120)), Has.Length.LessThanOrEqualTo(63));
    }
}

public class HostnameTests
{
    [Test]
    public void A_pasted_url_is_accepted_and_reduced_to_its_host()
    {
        Assert.That(Hostname.TryNormalize("https://Example.com/pricing?x=1"), Is.EqualTo("example.com"));
    }

    [Test]
    public void A_trailing_dot_and_case_are_normalized()
    {
        Assert.That(Hostname.TryNormalize("Shop.Example.COM."), Is.EqualTo("shop.example.com"));
    }

    /// <summary>
    /// Punycode, because that is what DNS holds and what the provider answers with. Comparing a unicode
    /// hostname against a punycode one is a domain that looks connected twice and works zero times.
    /// </summary>
    [Test]
    public void An_internationalized_hostname_becomes_punycode()
    {
        Assert.That(Hostname.TryNormalize("kávézó.hu"), Is.EqualTo("xn--kvz-slad2ta.hu"));
    }

    [Test]
    public void Things_that_are_not_hostnames_are_refused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Hostname.TryNormalize("localhost"), Is.Null, "no dot");
            Assert.That(Hostname.TryNormalize("me@example.com"), Is.Null);
            Assert.That(Hostname.TryNormalize("two words.com"), Is.Null);
            Assert.That(Hostname.TryNormalize("-example.com"), Is.Null);
        });
    }
}
