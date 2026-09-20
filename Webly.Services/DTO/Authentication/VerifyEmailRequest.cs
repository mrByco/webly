using System.ComponentModel.DataAnnotations;

namespace Webly.Services.DTO.Authentication;

public record VerifyEmailRequest
{
    [Required]
    public required string Token { get; init; }
}
