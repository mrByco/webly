using System.Text.Json;
using System.Text.Json.Serialization;

namespace Webly.Data.Models.Sites.Document;

/// <summary>
/// A whole website, as data. This is the only representation of a site's content: there is no HTML,
/// JSX or template source anywhere in Webly that a person or the agent edits. See CLAUDE.md "The site
/// document" for why a structured document beat generated source files.
///
/// Stored as jsonb inside a <see cref="SiteVersion"/> and never edited in place — a change produces a
/// new version holding a new document. <see cref="Clone"/> is how a turn starts from the current one.
/// </summary>
public class SiteDocument
{
    /// <summary>
    /// Bumped when a change to this shape cannot be read by older code. Nothing migrates on read
    /// today; it exists so that the first time a section's props are restructured, the migration has
    /// something to switch on instead of guessing from the contents of a jsonb column.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    public SiteTheme Theme { get; set; } = new();

    public SiteNavigation Navigation { get; set; } = new();

    /// <summary>
    /// The pages, in the order the editor lists them. The home page is the one whose
    /// <see cref="SitePage.Path"/> is <c>/</c>; there is no <c>IsHome</c> flag beside the path,
    /// because two fields that must agree eventually do not.
    /// </summary>
    public List<SitePage> Pages { get; set; } = [];

    public SitePage? FindPage(string pageId) => Pages.FirstOrDefault(x => x.Id == pageId);

    public SitePage? FindPageByPath(string path) =>
        Pages.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A deep copy, by serializing and reading back.
    ///
    /// Not a convenience: a section's props are a <see cref="System.Text.Json.Nodes.JsonObject"/>, and
    /// a <c>JsonNode</c> remembers its parent — so a member-wise copy would either throw when the
    /// copy's props were attached to a second document or, worse, hand two versions the same mutable
    /// node and let an edit to the draft silently rewrite what the published version says. A
    /// round trip through JSON has neither problem and costs microseconds on a document this size.
    /// </summary>
    public SiteDocument Clone() =>
        JsonSerializer.Deserialize<SiteDocument>(JsonSerializer.Serialize(this, SerializerOptions), SerializerOptions)
        ?? throw new InvalidOperationException("A site document failed to round-trip through JSON.");

    /// <summary>
    /// The one serializer configuration for documents, used by the jsonb value converter, by
    /// <see cref="Clone"/> and by the renderer. Camel case because the same shape crosses the API to
    /// TypeScript, and enums as names for the reason given in <c>Program.cs</c>.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
