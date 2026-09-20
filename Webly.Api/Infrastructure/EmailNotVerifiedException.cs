namespace Webly.Api.Infrastructure;

/// <summary>
/// Thrown when a signed-in user reaches something that needs a proven email address.
///
/// Translated to 403 by <see cref="Middleware.ExceptionHandlingMiddleware"/> with a machine-readable
/// code, so the client can show "verify your address" rather than a generic failure.
/// </summary>
public class EmailNotVerifiedException() : Exception("This action requires a verified email address.");
