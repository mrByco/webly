namespace Webly.Services.DTO.Forms;

/// <summary>One message a visitor sent, as the editor shows it.</summary>
public record FormSubmissionResponse
{
    public required string Nanoid { get; init; }

    /// <summary>Which form on the site it came from.</summary>
    public required string FormName { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// The fields in the order the visitor's browser sent them. A list of pairs rather than a dictionary,
    /// because a form may legitimately send the same name twice — a set of checkboxes does — and a
    /// dictionary would silently keep one of them.
    /// </summary>
    public required IReadOnlyList<FormFieldResponse> Fields { get; init; }
}

public record FormFieldResponse
{
    public required string Name { get; init; }

    public required string Value { get; init; }
}
