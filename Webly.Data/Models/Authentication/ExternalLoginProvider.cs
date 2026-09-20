namespace Webly.Data.Models.Authentication;

/// <summary>
/// An identity provider a user can sign in through. An enum rather than a free string so adding a
/// provider is a compile-time change with a migration behind it, not a value someone can typo.
/// Persisted as text, like <see cref="SecurityTokenPurpose"/>.
/// </summary>
public enum ExternalLoginProvider
{
    Google
}
