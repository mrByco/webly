using System.Text.Json.Nodes;

namespace Webly.Services.DTO.Sites;

/// <summary>
/// A hand edit from the property editor. The same patch semantics as the agent's <c>UpdateSection</c> tool, and
/// through the same <c>SiteDraft</c> — one implementation of "edit a section", whoever is doing the editing.
/// </summary>
public record EditSectionRequest
{
    public required string SectionId { get; init; }

    /// <summary>Just the fields that changed. Absent fields are left alone; a null value clears one.</summary>
    public required JsonObject Props { get; init; }
}
