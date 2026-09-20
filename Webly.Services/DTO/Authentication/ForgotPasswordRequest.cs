using System.ComponentModel.DataAnnotations;

namespace Webly.Services.DTO.Authentication;

public record ForgotPasswordRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }
}
