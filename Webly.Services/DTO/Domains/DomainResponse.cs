using Webly.Data.Models.Sites;

namespace Webly.Services.DTO.Domains;

/// <summary>
/// A hostname and what the person has to do about it. The DNS record fields are the provider's own words, carried
/// through unchanged — the screen's job here is to be copy-pasteable into a registrar's panel.
/// </summary>
public record DomainResponse
{
    public required string Nanoid { get; init; }
    public required string Hostname { get; init; }
    public required DomainVerificationState VerificationState { get; init; }
    public required bool IsPrimary { get; init; }

    public string? DnsRecordType { get; init; }
    public string? DnsRecordName { get; init; }
    public string? DnsRecordValue { get; init; }

    public DateTime? VerifiedAt { get; init; }
    public string? LastError { get; init; }
}
