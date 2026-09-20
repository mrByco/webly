using Webly.Data.Models.Sites.Document;
using Webly.Services.Services.Rendering;
using Webly.Services.Services.Rendering.Sections;
using Webly.Services.Services.Sites;

namespace Webly.Tests;

/// <summary>
/// The catalogue's own invariants. These are the tests that make "adding a section type is four edits"
/// safe: three of the four are checked here, so a half-added type fails the suite instead of rendering
/// as a blank band on somebody's published page.
/// </summary>
public class SectionCatalogueTests
{
    private static readonly ISectionRenderer[] Renderers =
    [
        new HeroSectionRenderer(),
        new RichTextSectionRenderer(),
        new FeatureGridSectionRenderer(),
        new TestimonialsSectionRenderer(),
        new FaqSectionRenderer(),
        new CtaSectionRenderer()
    ];

    [Test]
    public void Every_section_type_has_a_schema_and_a_renderer()
    {
        foreach (var type in Enum.GetValues<SectionType>())
        {
            Assert.That(() => SectionCatalogue.For(type), Throws.Nothing, $"{type} has no schema.");

            Assert.That(
                Renderers.Any(renderer => renderer.Type == type),
                Is.True,
                $"{type} is in the catalogue but nothing can draw it.");
        }
    }

    /// <summary>
    /// A new section has to be valid the moment it exists, or "add it, then fill it in" — which is how
    /// both a person and the agent work — would be impossible.
    /// </summary>
    [Test]
    public void A_new_section_of_every_type_produces_a_valid_document()
    {
        foreach (var type in Enum.GetValues<SectionType>())
        {
            var draft = SiteDraft.From(StarterTemplate.For("Test"));
            var pageId = draft.Document.Pages[0].Id;

            var result = draft.AddSection(pageId, type);

            Assert.That(result.Succeeded, Is.True, $"adding a {type} was rejected: {result.Message}");
        }
    }

    [Test]
    public void The_catalogue_description_names_every_type_and_field()
    {
        var described = SectionCatalogue.Describe();

        foreach (var schema in SectionCatalogue.All)
        {
            Assert.That(described, Does.Contain(schema.Type.ToString()));

            foreach (var field in schema.Fields)
                Assert.That(described, Does.Contain(field.Name), $"{schema.Type}.{field.Name} is not in the agent's instructions.");
        }
    }
}
