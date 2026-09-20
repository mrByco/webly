namespace Webly.Services.DTO.Sites;

/// <summary>
/// Everything that can go wrong with a site operation. <c>NotFound</c> covers "does not exist" and "is not
/// yours" deliberately — see <c>SiteController.Failure</c>, which is the one place these become status codes.
/// </summary>
public enum SiteError
{
    NotFound,

    /// <summary>The account is at its plan's site limit.</summary>
    LimitReached,

    /// <summary>A name that is only whitespace, or longer than a name should be.</summary>
    InvalidName,

    /// <summary>The version named for a restore does not belong to this site.</summary>
    VersionNotFound,

    /// <summary>The edit would have produced an invalid document; the problems say why.</summary>
    InvalidDocument
}
