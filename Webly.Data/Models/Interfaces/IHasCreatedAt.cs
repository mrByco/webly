namespace Webly.Data.Models.Interfaces;

/// <summary>
/// An entity that records when it happened and is then never touched again — a version, a message in a
/// thread, a form submission somebody's visitor sent.
///
/// Deliberately not the same thing as <see cref="IHasTimestamps"/>, and deliberately not its base. An
/// <c>UpdatedAt</c> on a row that cannot be updated is a field that can only ever say something
/// misleading, and the distinction is worth a second interface because it is the one fact about these
/// rows that stops somebody adding an edit endpoint for them. What the two share is the clock:
/// <see cref="WeblyDbContext"/> stamps both in the same method from the same <c>now</c>, so a version
/// and the message that produced it agree exactly.
/// </summary>
public interface IHasCreatedAt
{
    DateTime CreatedAt { get; set; }
}
