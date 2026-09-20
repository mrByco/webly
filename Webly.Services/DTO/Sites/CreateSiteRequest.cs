namespace Webly.Services.DTO.Sites;

public record CreateSiteRequest
{
    /// <summary>What the person calls it. The slug is derived from this and never asked for.</summary>
    public required string Name { get; init; }
}
