namespace Webly.Data.Models.Usage;

/// <summary>How the thing that cost money ended. A sandbox is always <see cref="Completed"/>.</summary>
public enum UsageOutcome
{
    Completed,
    Failed,
    Stopped
}
