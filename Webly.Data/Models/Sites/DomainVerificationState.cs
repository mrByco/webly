namespace Webly.Data.Models.Sites;

/// <summary>
/// Where a custom hostname is in the handover from its registrar to us. Mirrored from the provider,
/// never inferred locally — see <see cref="Domain"/>.
/// </summary>
public enum DomainVerificationState
{
    /// <summary>Added, DNS not yet pointing here. The state a person spends a coffee break in.</summary>
    Pending,

    /// <summary>The provider confirmed the records and issued a certificate. The site serves here.</summary>
    Verified,

    /// <summary>
    /// The provider refused: the records are wrong, or the hostname belongs to somebody else's
    /// account. Distinct from <see cref="Pending"/> because the person has something to fix rather
    /// than something to wait for, and <c>Domain.LastError</c> says what.
    /// </summary>
    Failed
}
