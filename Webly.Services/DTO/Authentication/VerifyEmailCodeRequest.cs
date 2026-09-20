using System.ComponentModel.DataAnnotations;

namespace Webly.Services.DTO.Authentication;

public record VerifyEmailCodeRequest
{
    /// <summary>The six digits from the verification email.</summary>
    [Required, RegularExpression(@"^\d{6}$")]
    public required string Code { get; init; }
}
