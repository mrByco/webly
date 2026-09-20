using Webly.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Webly.Api.Middleware;

/// <summary>
/// Turns the exceptions that mean something specific to a caller into the right status code, in one
/// place, so controllers do not each have to remember.
///
/// Anything not listed here is left alone and becomes the usual 500 — this is a translator for
/// expected conditions, not a catch-all that hides bugs.
/// </summary>
public class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (EmailNotVerifiedException)
        {
            if (context.Response.HasStarted)
            {
                logger.LogWarning("Email verification was required after the response had started.");
                throw;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Confirm your email address before doing that.",
                // What the client branches on. The title is for humans and may be reworded.
                Extensions = { ["code"] = "email_not_verified" }
            });
        }
    }
}
