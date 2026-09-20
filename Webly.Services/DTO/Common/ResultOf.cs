namespace Webly.Services.DTO.Common;

/// <summary>
/// A use case's answer when success carries a value: the error enum of its area, or the value.
///
/// The non-generic <see cref="Result{TError}"/> beside it is for the operations that answer 204. Two records rather
/// than one with a nullable value, so that a caller cannot read a value that a failure never produced.
/// </summary>
public record Result<TError, TValue> where TError : struct, Enum
{
    public required bool Succeeded { get; init; }
    public TError Error { get; init; }
    public TValue? Value { get; init; }

    /// <summary>
    /// One sentence about this particular failure, when the enum alone does not say enough — the validator's
    /// problems, a provider's refusal. It reaches the client as the problem detail, which is what makes the
    /// difference between "that edit was rejected" and "the headline is 134 characters; at most 120 are allowed".
    /// </summary>
    public string? Detail { get; init; }

    public static Result<TError, TValue> Ok(TValue value) => new() { Succeeded = true, Value = value };

    public static Result<TError, TValue> Fail(TError error, string? detail = null) =>
        new() { Succeeded = false, Error = error, Detail = detail };
}
