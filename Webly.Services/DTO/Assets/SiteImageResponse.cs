namespace Webly.Services.DTO.Assets;

/// <summary>An image in the site's own source, as the editor lists it.</summary>
public record SiteImageResponse
{
    /// <summary>
    /// How the site's own pages refer to it: <c>/images/hero.jpg</c>. Not the repository path
    /// (<c>public/images/hero.jpg</c>) — what a person copies into a message has to be what the agent puts in
    /// a <c>src</c>, and Next.js serves everything under <c>public/</c> from the root.
    /// </summary>
    public required string Url { get; init; }

    public required string FileName { get; init; }

    public required long Bytes { get; init; }
}

/// <summary>What an upload produced: the images, and the version that now holds them.</summary>
public record UploadImagesResponse
{
    public required IReadOnlyList<SiteImageResponse> Images { get; init; }

    /// <summary>The version the upload committed, or null when every file was already in the site unchanged.</summary>
    public string? VersionNanoid { get; init; }
}
