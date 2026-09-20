namespace Webly.Services.Services.Sites;

using Webly.Data.Models.Sites.Document;

/// <summary>One section type's declared shape. See <see cref="SectionCatalogue"/>.</summary>
public record SectionSchema(
    SectionType Type,

    /// <summary>What the editor calls it.</summary>
    string Label,

    /// <summary>
    /// What the section is for, in one sentence. Goes into the agent's tool description, so it has to
    /// say when to reach for this section rather than what it looks like.
    /// </summary>
    string Purpose,

    IReadOnlyList<SectionFieldSchema> Fields);
