using System.ComponentModel.DataAnnotations;

namespace Webly.Services.DTO.Authentication;

public record ResetPasswordRequest
{
    [Required]
    public required string Token { get; init; }

    [Required, MinLength(8), MaxLength(256)]
    public required string NewPassword { get; init; }
}
