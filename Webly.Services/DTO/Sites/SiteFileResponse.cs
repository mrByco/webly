namespace Webly.Services.DTO.Sites;

/// <summary>A file in the site's repository, for the editor's code view.</summary>
public record SiteFileResponse
{
    public required string Path { get; init; }

    /// <summary>
    /// The file's text. Null for something that is not text — an image, a font — where the client shows the
    /// path and its size rather than pretending to render it.
    /// </summary>
    public string? Text { get; init; }

    public required long Size { get; init; }
}

/// <summary>A listing: what is in the site, without the contents.</summary>
public record SiteFileEntryResponse
{
    public required string Path { get; init; }
    public required long Size { get; init; }
}
