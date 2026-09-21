namespace Webly.Services.DTO.Authentication;

/// <summary>Closing an account, which asks for the password again because it cannot be undone.</summary>
public record DeleteAccountRequest
{
    /// <summary>
    /// Null is allowed and means "this account has no password" — it was created through Google, so there is
    /// nothing to prove knowledge of. An account that does have one and sends the wrong one is refused.
    /// </summary>
    public string? CurrentPassword { get; init; }
}
