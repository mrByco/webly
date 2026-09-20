namespace Webly.Api.Options;

/// <summary>
/// Google OAuth credentials, from the <c>Authentication:Google</c> section. Supplied through user
/// secrets in development and the environment in production — never <c>appsettings.json</c>, which
/// is in git.
///
/// Unlike the JWT options these are not required: the app boots, tests run and email+password login
/// works without them. <see cref="IsConfigured"/> is what every Google code path checks first, so an
/// unconfigured deployment simply has no Google button rather than a broken one.
/// </summary>
public class GoogleAuthOptions
{
    public const string SectionName = "Authentication:Google";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
