using System.Text.Json.Nodes;

namespace Webly.Data.Models.Sites.Document;

/// <summary>
/// One band of a page: a hero, a pricing table, a gallery. A section is a <see cref="SectionType"/>
/// plus a bag of props, and the props' shape is declared once in <c>SectionCatalogue</c> — the
/// schema there is what validates a write, tells the agent which fields exist, drives the property
/// editor in the client, and is what the renderer's template for this type reads.
///
/// Props are untyped here on purpose. The alternative — a C# record per section type in a
/// polymorphic hierarchy — spreads every new section across a payload class, a converter entry, a
/// DTO, a renderer template and a client form, and the reference project (auto-grader) records
/// exactly that: five registration points, where missing one is a blank space on a page. One
/// declarative schema with a generic carrier leaves the template as the only per-type code.
/// </summary>
public class SiteSection
{
    /// <summary>
    /// Stable identity across versions, so a tool call, a comment or a deep link can name this
    /// section after its neighbours have moved. A nanoid, minted where the section is created.
    /// </summary>
    public required string Id { get; set; }

    public required SectionType Type { get; set; }

    /// <summary>
    /// The section's content and settings, keyed by the field names in this type's schema. Validated
    /// on every write: an unknown key, a missing required field or a value of the wrong kind is
    /// rejected, so a document that is in the database renders.
    /// </summary>
    public JsonObject Props { get; set; } = new();

    /// <summary>
    /// Hides the section without deleting it. The person who asked the agent for "a testimonials
    /// block, actually no, keep it for later" gets what they meant, and the renderer skips it.
    /// </summary>
    public bool Hidden { get; set; }
}
