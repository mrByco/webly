namespace Webly.Services.DTO.Domains;

public enum DomainError
{
    SiteNotFound,
    DomainNotFound,

    /// <summary>Not a hostname: a URL with a path, an email address, something with a space in it.</summary>
    InvalidHostname,

    /// <summary>Already connected — to this site, or to somebody else's. Deliberately one error: which of the
    /// two it is would tell a stranger whether a domain is hosted here.</summary>
    AlreadyConnected,

    /// <summary>The provider refused. The message from it is carried on the domain row.</summary>
    ProviderRefused,

    /// <summary>Only a verified domain may be made primary — an unverified one does not resolve here.</summary>
    NotVerified,

    /// <summary>The primary domain cannot simply be removed; promote another one first.</summary>
    IsPrimary
}
