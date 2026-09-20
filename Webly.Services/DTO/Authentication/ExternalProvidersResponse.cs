namespace Webly.Services.DTO.Authentication;

/// <summary>
/// Which external sign-in options this deployment can actually perform. The login page renders its
/// buttons from this rather than assuming: an environment without Google credentials should show no
/// Google button, not one that fails when clicked.
/// </summary>
public record ExternalProvidersResponse
{
    public required bool Google { get; init; }
}
