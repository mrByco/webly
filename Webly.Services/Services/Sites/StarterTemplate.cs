using NanoidDotNet;
using System.Text.Json.Nodes;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Sites;

/// <summary>
/// The document a brand-new site starts as.
///
/// A site is never empty, and that is a product decision rather than a convenience. An empty canvas
/// makes the first chat turn look like it failed — there is nothing to preview, nothing to point at and
/// no example of the shape the agent is meant to produce. Starting from a real page means the first
/// thing a person ever types ("we're a bike shop in Utrecht, open since 2009") is an <i>edit</i>, which
/// is what the agent is good at, instead of a creation from nothing, which is where it invents.
///
/// One template, deliberately. A gallery of twenty starters is a browsing UI, a thumbnail pipeline and a
/// decision to make before anybody has typed a sentence — and the agent restructures the page on the
/// first turn anyway, so the starter's job is to be a valid, sensible skeleton and nothing more.
/// </summary>
public static class StarterTemplate
{
    /// <summary>
    /// A one-page site with a hero, three features and a closing call to action, worded as placeholders
    /// that are obviously placeholders. Nothing here pretends to be real copy: a person who publishes
    /// before editing should see that they published a skeleton, not a site claiming to be a business
    /// that does not exist.
    /// </summary>
    public static SiteDocument For(string siteName)
    {
        var homeId = Nanoid.Generate(size: 10);

        return new SiteDocument
        {
            Theme = new SiteTheme(),
            Navigation = new SiteNavigation
            {
                Header = [new NavigationLink { Label = "Home", PageId = homeId }],
                FooterNote = $"© {{year}} {siteName}"
            },
            Pages =
            [
                new SitePage
                {
                    Id = homeId,
                    Path = "/",
                    Title = "Home",
                    Seo = new PageSeo(),
                    Sections =
                    [
                        Section(SectionType.Hero, new JsonObject
                        {
                            ["headline"] = siteName,
                            ["subheadline"] = "Tell the agent what this site is about and it will rewrite this page for you.",
                            ["layout"] = "textOnly"
                        }),
                        Section(SectionType.FeatureGrid, new JsonObject
                        {
                            ["title"] = "What we do",
                            ["items"] = new JsonArray
                            {
                                Feature("First thing", "Describe it in one sentence.", "star"),
                                Feature("Second thing", "Describe it in one sentence.", "check"),
                                Feature("Third thing", "Describe it in one sentence.", "spark")
                            }
                        }),
                        Section(SectionType.Cta, new JsonObject
                        {
                            ["headline"] = "Get in touch",
                            ["body"] = "Say how people should reach you.",
                            ["actionLabel"] = "Email us",
                            ["actionHref"] = "mailto:hello@example.com",
                            ["tone"] = "solid"
                        })
                    ]
                }
            ]
        };
    }

    private static SiteSection Section(SectionType type, JsonObject props)
    {
        var merged = SectionCatalogue.DefaultProps(type);

        foreach (var (key, value) in props)
            merged[key] = value?.DeepClone();

        return new SiteSection { Id = Nanoid.Generate(size: 10), Type = type, Props = merged };
    }

    private static JsonObject Feature(string title, string description, string icon) => new()
    {
        ["title"] = title,
        ["description"] = description,
        ["icon"] = icon
    };
}
