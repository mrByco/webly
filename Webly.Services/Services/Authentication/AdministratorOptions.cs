namespace Webly.Services.Services.Authentication;

/// <summary>
/// Who maintains the global catalog, from the <c>Administrators</c> configuration section.
///
/// Configuration is the whole authority here and nothing is stored on the user. <see cref="User"/>
/// already carries a <c>Roles</c> array naming <c>admin</c>; a second column beside it would be two
/// places that must agree, which is the defect this project rejects everywhere else. A startup
/// seeder writing the role instead has a subtler bug: it can only reach users who already exist, so
/// a configured administrator who registers afterwards silently never becomes one.
///
/// An empty list is a legitimate deployment — nobody can edit the global catalog and the seeded
/// data still works — so this is deliberately not validated at startup.
/// </summary>
public class AdministratorOptions
{
    public const string SectionName = "Administrators";

    /// <summary>Compared normalized, so the casing in configuration does not matter.</summary>
    public IReadOnlyList<string> Emails { get; set; } = [];
}
