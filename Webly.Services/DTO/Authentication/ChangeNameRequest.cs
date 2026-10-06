using System.ComponentModel.DataAnnotations;

namespace Webly.Services.DTO.Authentication;

/// <summary>The name to be called by — the same rule registration applies, so a name either screen accepts the other does.</summary>
public record ChangeNameRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public required string DisplayName { get; init; }
}
