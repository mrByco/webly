using System.ComponentModel.DataAnnotations;

namespace Webly.Services.DTO.Authentication;

public record LoginRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}
