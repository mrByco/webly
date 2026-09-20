namespace Webly.Services.DTO.Authentication;

public enum AuthError
{
    None,

    /// <summary>
    /// Wrong password, unknown email, or an account that has no password because it was created
    /// through a provider. One value for all three on purpose: telling them apart tells an
    /// attacker which addresses are registered.
    /// </summary>
    InvalidCredentials,

    EmailAlreadyRegistered,

    /// <summary>The provider did not confirm the address, so it cannot be trusted to identify an account.</summary>
    ExternalEmailNotVerified,

    /// <summary>A mailed token was unknown, already used, or past its expiry.</summary>
    InvalidToken,

    /// <summary>The action needs a proven email address and this one is not proven yet.</summary>
    EmailNotVerified
}
