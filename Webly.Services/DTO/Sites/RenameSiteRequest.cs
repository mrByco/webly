namespace Webly.Services.DTO.Sites;

public record RenameSiteRequest
{
    public required string Name { get; init; }

    /// <summary>
    /// Whether the site itself should say the new name too, as a version.
    ///
    /// It is a choice rather than a consequence, because the two names are genuinely different things: this
    /// one is the label on the dashboard, and the one in <c>src/site.ts</c> is content — the header, the
    /// footer and the title of every page somebody shares. Usually they are the same fact typed once, which
    /// is why the screen offers it ticked; but a site whose pages the agent has since reworded is one where
    /// the dashboard label is just a label, and rewriting its source from a settings field would be an edit
    /// nobody asked for.
    /// </summary>
    public bool ApplyToSite { get; init; }
}
