namespace Webly.Services.Services.Sites;

/// <summary>
/// What one edit to a <see cref="SiteDraft"/> did. Carries prose rather than an error enum because both
/// of its readers are readers of prose: the agent, which gets this back as a tool result and has to fix
/// its own mistake from it, and the property editor, which shows it to the person.
/// </summary>
public record DraftResult
{
    public required bool Succeeded { get; init; }

    /// <summary>What went wrong, one sentence per problem. Empty on success.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>
    /// The id of whatever was created, when something was. The agent needs it to fill the thing it just
    /// added, and getting it back from the same call is what saves a round trip per section.
    /// </summary>
    public string? Id { get; init; }

    public static DraftResult Ok(string? id = null) => new() { Succeeded = true, Id = id };

    public static DraftResult Fail(params string[] problems) =>
        new() { Succeeded = false, Problems = problems };

    public static DraftResult Fail(IReadOnlyList<string> problems) =>
        new() { Succeeded = false, Problems = problems };

    /// <summary>One string, for a tool result or a toast.</summary>
    public string Message => Succeeded ? "Done." : string.Join(" ", Problems);
}
