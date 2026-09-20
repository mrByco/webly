namespace Webly.Services.DTO.Domains;

public record AddDomainRequest
{
    /// <summary>A hostname. Normalised on the way in, so <c>https://Example.com/</c> is accepted.</summary>
    public required string Hostname { get; init; }
}
