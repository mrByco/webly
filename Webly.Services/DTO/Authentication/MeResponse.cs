namespace Webly.Services.DTO.Authentication;

/// <summary>
/// Who the caller is, as far as the client needs to know. Returned for both signed-in and
/// signed-out callers — <see cref="IsAuthenticated"/> is the discriminator — so the client can ask
/// "who am I" without treating a normal answer as an error.
/// </summary>
public record MeResponse
{
    public required bool IsAuthenticated { get; init; }

    public string? Nanoid { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public string? ProfilePictureUrl { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Whether a password is set — the account may be Google-only.</summary>
    public bool HasPassword { get; init; }

    /// <summary>
    /// Whether the address has been proven. False means nothing in the app is reachable — the
    /// verification screen is the only thing that renders. See CLAUDE.md "Email verification".
    /// </summary>
    public bool EmailVerified { get; init; }

    /// <summary>Providers already linked, so the profile UI can show what to add or remove.</summary>
    public IReadOnlyList<string> LinkedProviders { get; init; } = [];

    /// <summary>
    /// False for a freshly verified account, which is what sends them to the first-site screen:
    /// until a site exists there is nothing for the editor to open and nothing for the agent to edit.
    /// </summary>
    public bool HasSite { get; init; }

    public string? CurrentSiteNanoid { get; init; }

    /// <summary>
    /// The open site's name, so the header can show it without a second round trip. Its pages,
    /// versions and domains are not here — those belong to the site endpoints.
    /// </summary>
    public string? CurrentSiteName { get; init; }

    public static MeResponse Anonymous { get; } = new() { IsAuthenticated = false };
}
