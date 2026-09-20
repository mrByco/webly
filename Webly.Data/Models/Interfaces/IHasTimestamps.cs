namespace Webly.Data.Models.Interfaces;

/// <summary>
/// An entity whose creation and last-write times are worth knowing.
/// <see cref="WeblyDbContext"/> stamps both, so the values do not depend on each call site
/// remembering. One timestamp per batch, so rows saved together agree exactly.
/// </summary>
public interface IHasTimestamps
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
