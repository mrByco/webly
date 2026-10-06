namespace Webly.Data.Repositories;

/// <summary>
/// The first half of every Postgres advisory lock this application takes, one per kind of thing locked, so that a
/// lock on conversation 7 and a lock on the sites of user 7 are different locks. In one place because the only
/// property any of them needs is that no two are equal, and two private constants in two files cannot see each
/// other. The values are arbitrary.
/// </summary>
internal static class AdvisoryLocks
{
    /// <summary>One conversation's message sequence: <c>ConversationRepository.AppendMessageAsync</c>.</summary>
    public const int ConversationMessages = 8_141;

    /// <summary>One owner's set of sites: <c>SiteRepository.LockOwnerAsync</c>.</summary>
    public const int OwnerSites = 8_142;
}
