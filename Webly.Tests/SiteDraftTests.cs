using System.Text.Json.Nodes;
using Webly.Data.Models.Sites.Document;
using Webly.Services.Services.Sites;

namespace Webly.Tests;

/// <summary>
/// The editing rules, which are the same for the agent and for the property editor because both go
/// through <see cref="SiteDraft"/>.
/// </summary>
public class SiteDraftTests
{
    private static SiteDraft NewDraft() => SiteDraft.From(StarterTemplate.For("Kovacs Bakery"));

    [Test]
    public void A_rejected_edit_changes_nothing()
    {
        var draft = NewDraft();
        var before = draft.Document.Pages[0].Sections.Count;

        var result = draft.AddSection("no-such-page", SectionType.Cta);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(draft.IsDirty, Is.False, "a failed edit must not leave the draft dirty");
            Assert.That(draft.Document.Pages[0].Sections, Has.Count.EqualTo(before));
        });
    }

    /// <summary>
    /// The invariant the rest of the system leans on: every operation validates the whole document and
    /// keeps it only if it is still valid. Here a required field is emptied, which the validator refuses.
    /// </summary>
    [Test]
    public void An_edit_that_would_break_the_document_is_refused_with_a_reason()
    {
        var draft = NewDraft();
        var hero = draft.Document.Pages[0].Sections.First(x => x.Type == SectionType.Hero);

        var result = draft.UpdateSection(hero.Id, new JsonObject { ["headline"] = "" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("headline"));
            Assert.That(draft.Document.Pages[0].Sections.First(x => x.Id == hero.Id).Props["headline"]!.GetValue<string>(),
                Is.EqualTo("Kovacs Bakery"),
                "the old value has to survive a refused edit");
        });
    }

    [Test]
    public void Updating_a_section_is_a_patch_and_leaves_other_fields_alone()
    {
        var draft = NewDraft();
        var hero = draft.Document.Pages[0].Sections.First(x => x.Type == SectionType.Hero);
        var subheadline = hero.Props["subheadline"]!.GetValue<string>();

        var result = draft.UpdateSection(hero.Id, new JsonObject { ["headline"] = "Fresh bread daily" });
        var updated = draft.Document.Pages[0].Sections.First(x => x.Id == hero.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(updated.Props["headline"]!.GetValue<string>(), Is.EqualTo("Fresh bread daily"));
            Assert.That(updated.Props["subheadline"]!.GetValue<string>(), Is.EqualTo(subheadline));
        });
    }

    [Test]
    public void A_page_path_is_normalized_rather_than_refused()
    {
        var draft = NewDraft();

        var result = draft.AddPage("About Us/", "About");

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(draft.Document.FindPage(result.Id!)!.Path, Is.EqualTo("/about us"));
        });
    }

    [Test]
    public void Two_pages_cannot_share_a_path()
    {
        var draft = NewDraft();
        draft.AddPage("about", "About");

        var result = draft.AddPage("/about", "About again");

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("/about"));
        });
    }

    [Test]
    public void The_home_page_cannot_be_deleted()
    {
        var draft = NewDraft();
        var home = draft.Document.FindPageByPath("/")!;

        Assert.That(draft.RemovePage(home.Id).Succeeded, Is.False);
    }

    /// <summary>
    /// A menu link names a page by id, so renaming the page's path must not break it. This is the whole
    /// reason links are not stored as paths.
    /// </summary>
    [Test]
    public void A_menu_link_survives_its_page_being_renamed()
    {
        var draft = NewDraft();
        var home = draft.Document.FindPageByPath("/")!;
        var added = draft.AddPage("prices", "Prices");

        draft.SetNavigation(new SiteNavigation
        {
            Header =
            [
                new NavigationLink { Label = "Home", PageId = home.Id },
                new NavigationLink { Label = "Prices", PageId = added.Id! }
            ]
        });

        var renamed = draft.UpdatePage(added.Id!, "pricing", null, null);

        Assert.Multiple(() =>
        {
            Assert.That(renamed.Succeeded, Is.True, renamed.Message);
            Assert.That(draft.Document.Navigation.Header[1].PageId, Is.EqualTo(added.Id));
            Assert.That(draft.Document.FindPage(added.Id!)!.Path, Is.EqualTo("/pricing"));
        });
    }

    [Test]
    public void A_navigation_link_naming_both_a_page_and_a_url_is_refused()
    {
        var draft = NewDraft();
        var home = draft.Document.FindPageByPath("/")!;

        var result = draft.SetNavigation(new SiteNavigation
        {
            Header = [new NavigationLink { Label = "Home", PageId = home.Id, Url = "https://example.com" }]
        });

        Assert.That(result.Succeeded, Is.False);
    }

    /// <summary>
    /// The draft starts as a clone, so the version it came from cannot be changed by editing. Without
    /// this, an abandoned turn would still have rewritten the stored document.
    /// </summary>
    [Test]
    public void Editing_a_draft_does_not_touch_the_document_it_came_from()
    {
        var original = StarterTemplate.For("Kovacs Bakery");
        var draft = SiteDraft.From(original);
        var hero = draft.Document.Pages[0].Sections.First(x => x.Type == SectionType.Hero);

        draft.UpdateSection(hero.Id, new JsonObject { ["headline"] = "Changed" });

        Assert.That(
            original.Pages[0].Sections.First(x => x.Type == SectionType.Hero).Props["headline"]!.GetValue<string>(),
            Is.EqualTo("Kovacs Bakery"));
    }

    [Test]
    public void The_summary_names_the_first_change_and_counts_the_rest()
    {
        var draft = NewDraft();
        draft.AddPage("about", "About");

        Assert.That(draft.Summarize(), Is.EqualTo("Added the page /about (About)"));

        draft.AddPage("prices", "Prices");

        Assert.That(draft.Summarize(), Does.Contain("and 1 more change"));
    }
}
