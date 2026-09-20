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

    /// <summary>The version named does not belong to this site.</summary>
    VersionNotFound,

    /// <summary>The file named is not in that commit.</summary>
    FileNotFound,

    /// <summary>The repository refused — a tree too large, a git failure. The detail says which.</summary>
    RepositoryFailed,

    /// <summary>
    /// No workspace could be started, so nothing can be edited or previewed right now. A capacity or
    /// configuration problem, never the person's fault, and the message says so.
    /// </summary>
    WorkspaceUnavailable
}
