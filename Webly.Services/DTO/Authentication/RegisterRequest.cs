using System.ComponentModel.DataAnnotations;

namespace Webly.Services.DTO.Authentication;

public record RegisterRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }

    /// <summary>
    /// Length is the only rule. Composition rules ("one digit, one symbol") push people towards
    /// shorter, more predictable passwords, and there is no password reset yet to rescue anyone
    /// who forgets an elaborate one.
    /// </summary>
    [Required, MinLength(8), MaxLength(256)]
    public required string Password { get; init; }

    [Required, MinLength(1), MaxLength(100)]
    public required string DisplayName { get; init; }
}
