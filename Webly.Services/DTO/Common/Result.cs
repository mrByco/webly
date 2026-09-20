namespace Webly.Services.DTO.Common;

/// <summary>
/// A use case's answer: success, or which of the area's errors happened.
///
/// One generic shape rather than a per-area copy: the reference projects grew four near-identical
/// result records before generalizing, and every one of them was the same twelve lines with the enum
/// name changed, read by controllers that only ever ask the same two questions.
/// </summary>
public record Result<TError> where TError : struct, Enum
{
    public required bool Succeeded { get; init; }
    public TError Error { get; init; }

    public static Result<TError> Ok() => new() { Succeeded = true };

    public static Result<TError> Fail(TError error) => new() { Succeeded = false, Error = error };
}

public record Result<TError, T> where TError : struct, Enum
{
    public required bool Succeeded { get; init; }
    public TError Error { get; init; }
    public T? Value { get; init; }

    public static Result<TError, T> Success(T value) => new() { Succeeded = true, Value = value };

    public static Result<TError, T> Fail(TError error) => new() { Succeeded = false, Error = error };
}
