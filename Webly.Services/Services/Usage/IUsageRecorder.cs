using Webly.Data.Models.Usage;
using Webly.Services.Agent;

namespace Webly.Services.Services.Usage;

/// <summary>
/// Writes down what something cost. One door for the three things that spend money — an agent turn, an editing
/// sandbox, a publish sandbox — so there is one definition of a usage row, as <c>CommitSiteVersion</c> is the one
/// definition of a version.
///
/// <b>Best-effort, by design.</b> Recording runs after the work it describes and must never fail it: a turn that
/// edited somebody's website is not undone because the usage table was unreachable. A failure is a warning in the
/// log and a gap in the report, which is the right way round.
/// </summary>
public interface IUsageRecorder
{
    Task RecordAsync(UsageEntry entry);
}

/// <summary>One thing that cost money, before it is a row.</summary>
public record UsageEntry(
    UsageKind Kind,
    UsageOutcome Outcome,
    int UserId,
    int? SiteId,
    string? SiteName,
    long DurationMs,
    decimal CostUsd,
    string? Agent = null,
    AgentUsage? Tokens = null,
    string? Description = null);
