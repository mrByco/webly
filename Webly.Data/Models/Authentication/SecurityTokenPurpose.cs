namespace Webly.Data.Models.Authentication;

/// <summary>
/// What a mailed single-use token is good for. Persisted as text, like
/// <see cref="ExternalLoginProvider"/>, and mixed into the token's hash so one purpose's token can
/// never be presented as another's — a verification link cannot be redeemed as a password reset.
///
/// Two values, and adding a third is a decision: every purpose here is a credential that arrives in
/// an inbox, and the hash mixing is the only thing keeping them apart.
/// </summary>
public enum SecurityTokenPurpose
{
    EmailVerification,
    PasswordReset
}
