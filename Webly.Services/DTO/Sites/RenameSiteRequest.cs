namespace Webly.Services.DTO.Sites;

public record RenameSiteRequest
{
    public required string Name { get; init; }
}
