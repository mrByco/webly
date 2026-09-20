namespace Webly.Services.DTO.Common;

/// <summary>
/// A use case's answer: success, or which of the area's errors happened, with an optional detail.
///
/// One generic shape rather than a per-area copy: the reference projects grew four near-identical
/// result records before generalizing, and every one of them was the same twelve lines with the enum
/// name changed, read by controllers that only ever ask the same two questions.
///
/// <see cref="Detail"/> is the third question, and it earns its place: an error enum says *which* rule was
/// broken, and some of them need to say *what* — a git failure, a build log's tail, the name of the file a
/// tree was refused for. It is for the person when a controller chooses to show it and for the log otherwise,
/// which is why it is a plain string and not a structured payload. Never null-checked by callers that do not
/// care: the value they pass to a ProblemDetails is allowed to be nothing.
/// </summary>
public record Result<TError> where TError : struct, Enum
{
    public required bool Succeeded { get; init; }
    public TError Error { get; init; }
    public string? Detail { get; init; }

    public static Result<TError> Ok() => new() { Succeeded = true };

    public static Result<TError> Fail(TError error, string? detail = null) =>
        new() { Succeeded = false, Error = error, Detail = detail };
}
