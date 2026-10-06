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

    /// <summary>
    /// A person changed a file themselves rather than asking the agent for it: the code view, when hand
    /// editing lands, and today the rename that also writes the new name into the site.
    /// </summary>
    Manual,

    /// <summary>An older version's tree written forward. See <c>SiteVersion.RestoredFromVersionId</c>.</summary>
    Restore,

    /// <summary>
    /// A person added files — today only images, committed into <c>public/images</c>.
    ///
    /// Its own value rather than <see cref="Manual"/>, because the history is read by the person who did it:
    /// "Added shopfront.jpg" beside a badge saying Manual would be describing the mechanism instead of what
    /// happened.
    /// </summary>
    Upload,

    /// <summary>
    /// Webly bringing a file it owns inside the site up to date — the editing instructions, the build settings, the
    /// contact form's components; see <c>SyncWeblyOwnedFiles</c>. Neither the person nor the agent wrote it, and the
    /// history has to be able to say so. These were <see cref="Template"/> until the history was read: every one of
    /// them said "created", the word for a site's very first commit, under "Updated the editing instructions".
    /// </summary>
    Webly
}
