using System.ComponentModel.DataAnnotations;

namespace Webly.Services.DTO.Authentication;

public record ChangePasswordRequest
{
    /// <summary>
    /// Optional only because a Google-created account has no password to confirm. When one exists it
    /// is required, and the use case enforces that — not this attribute.
    /// </summary>
    public string? CurrentPassword { get; init; }

    [Required, MinLength(8), MaxLength(256)]
    public required string NewPassword { get; init; }
}
