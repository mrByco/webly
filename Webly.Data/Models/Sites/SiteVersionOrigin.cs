namespace Webly.Data.Models.Sites;

/// <summary>
/// What caused a commit to exist. Persisted as text, and part of the history UI rather than
/// bookkeeping: "you asked for this", "you edited this yourself" and "you brought this back" read
/// differently to the person scrolling through their site's past.
/// </summary>
public enum SiteVersionOrigin
{
    /// <summary>The first commit of a site, from the starter template.</summary>
    Template,

    /// <summary>An agent session committed it. The usual case.</summary>
    Agent,

    /// <summary>A person edited a file directly, in the editor's code view.</summary>
    Manual,

    /// <summary>An older version's tree written forward. See <c>SiteVersion.RestoredFromVersionId</c>.</summary>
    Restore
}
