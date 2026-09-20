namespace Webly.Data.Models.Sites;

/// <summary>
/// What caused a version to exist. Persisted as text, and part of the history UI rather than
/// bookkeeping: "you asked for this" and "you clicked this yourself" read differently to the person
/// scrolling back through their site's past.
/// </summary>
public enum SiteVersionOrigin
{
    /// <summary>The first version of a site, copied from a starter template.</summary>
    Template,

    /// <summary>An agent turn committed it. The usual case.</summary>
    Agent,

    /// <summary>A person edited something directly in the editor, without asking the agent.</summary>
    Manual,

    /// <summary>An older version copied forward. See <c>SiteVersion.RestoredFromVersionId</c>.</summary>
    Restore
}
