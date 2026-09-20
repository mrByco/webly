namespace Webly.Services.DTO.Authentication;

/// <summary>
/// The outcome of a sign-in attempt. Carries the raw tokens for the caller to put in cookies, and
/// a <see cref="MeResponse"/> rather than the user entity — EF entities stop at
/// <c>Webly.Services</c>.
/// </summary>
public record AuthResult
{
    public required bool Succeeded { get; init; }
    public AuthError Error { get; init; } = AuthError.None;

    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public MeResponse? Me { get; init; }

    public static AuthResult Fail(AuthError error) => new() { Succeeded = false, Error = error };

    public static AuthResult Success(string accessToken, string refreshToken, MeResponse me) =>
        new() { Succeeded = true, AccessToken = accessToken, RefreshToken = refreshToken, Me = me };
}
