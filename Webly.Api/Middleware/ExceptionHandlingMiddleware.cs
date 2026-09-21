using Webly.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Webly.Services.Services.Repositories;

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
        catch (RepositoryConflictException conflict)
        {
            // The one repository failure that is the person's to act on: something else changed the site while
            // this change was being prepared, so nothing was written. A 409 rather than a 500, and the
            // exception's own sentence rather than a generic one, because that message was written to be read
            // by whoever pressed the button — pressing it again is the whole remedy.
            //
            // Centralized rather than mapped per use case, and that is the point: every operation that changes
            // a site goes through `CommitSiteVersion`, so every one of them can lose this race, and a caller
            // that forgot would answer a raw 500 with a stack trace in it. Which is exactly what uploading a
            // photograph did, the first time a commit really was refused.
            if (context.Response.HasStarted)
            {
                logger.LogWarning(conflict, "A repository conflict happened after the response had started.");
                throw;
            }

            logger.LogInformation(conflict, "A commit was refused because the branch had moved: {Detail}", conflict.Detail);

            context.Response.StatusCode = StatusCodes.Status409Conflict;

            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = conflict.Message,
                Extensions = { ["code"] = "site_changed" }
            });
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
