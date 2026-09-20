namespace Webly.Services.DTO.Sites;

/// <summary>
/// The section catalogue, for the client. The property editor is generated from this rather than hand-written per
/// section type — which is what keeps a section the agent added editable by hand the moment it exists, without a
/// second declaration of its fields in TypeScript that would go stale the first time a field moved.
/// </summary>
public record SectionSchemaResponse
{
    public required string Type { get; init; }
    public required string Label { get; init; }
    public required string Purpose { get; init; }
    public required IReadOnlyList<SectionFieldResponse> Fields { get; init; }
}

public record SectionFieldResponse
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Description { get; init; }
    public required bool Required { get; init; }
    public int? MaxLength { get; init; }
    public int? MaxItems { get; init; }
    public IReadOnlyList<string> Choices { get; init; } = [];
    public IReadOnlyList<SectionFieldResponse> ItemFields { get; init; } = [];
}
