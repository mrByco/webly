namespace Webly.Data;

/// <summary>
/// The one way an email address is turned into the form stored in <c>Users.Email</c>.
///
/// Registration, password login and external-login linking all have to agree on this, or the unique
/// index stops meaning "one account per address" and the same person ends up with two accounts
/// because one door lowercased and another did not.
/// </summary>
public static class NormalizedEmail
{
    public static string From(string email) => email.Trim().ToLowerInvariant();
}
