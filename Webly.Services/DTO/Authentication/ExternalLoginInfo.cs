using Webly.Data.Models.Authentication;

namespace Webly.Services.DTO.Authentication;

/// <summary>
/// What an external provider told us about the person signing in, normalized away from that
/// provider's claim names. Keeping the OAuth details in <c>Webly.Api</c> and passing this down
/// means the sign-in logic — and its tests — need no network and no provider credentials.
/// </summary>
public record ExternalLoginInfo
{
    public required ExternalLoginProvider Provider { get; init; }

    /// <summary>The provider's stable subject id.</summary>
    public required string ProviderKey { get; init; }

    public required string Email { get; init; }

    /// <summary>
    /// Whether the provider vouches that this person controls the address. Linking to an existing
    /// Webly account on an unverified address would let anyone claim it.
    /// </summary>
    public required bool EmailVerified { get; init; }

    public string? DisplayName { get; init; }

    public string? ProfilePictureUrl { get; init; }
}
