using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace Webly.Api.Infrastructure;

/// <summary>
/// The pages a visitor to a <i>customer's</i> website can land on at Webly's address: what the form endpoint answers
/// when there is nowhere better to send them.
///
/// Pages rather than status codes, because whoever sees one is a person who just pressed a button on somebody's
/// website. And pages rather than ProblemDetails, which is what the refusals used to be: a contact form is an ordinary
/// browser post, so "Nothing was filled in" arrived as <c>{"title":"Nothing was filled in…","status":400}</c> printed
/// across the visitor's screen, and a rate-limited one as a blank page. Self-contained and inline-styled, with no link
/// back — the site's own address is not this endpoint's business, and a wrong one would be worse than none.
/// </summary>
public static class VisitorPage
{
    public static ContentResult Result(int status, string title, string heading, string body) => new()
    {
        StatusCode = status,
        ContentType = "text/html; charset=utf-8",
        Content = Html(title, heading, body)
    };

    /// <summary>Written straight to a response, for the one caller that has no action result: the rate limiter.</summary>
    public static Task WriteAsync(HttpResponse response, int status, string title, string heading, string body)
    {
        response.StatusCode = status;
        response.ContentType = "text/html; charset=utf-8";

        return response.WriteAsync(Html(title, heading, body));
    }

    private static string Html(string title, string heading, string body) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="robots" content="noindex">
          <title>{{WebUtility.HtmlEncode(title)}}</title>
        </head>
        <body style="margin:0;font:400 16px/1.6 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;background:#faf7f2;color:#2b2320;">
          <main style="max-width:32rem;margin:0 auto;padding:4rem 1rem;text-align:center;">
            <h1 style="font-size:1.5rem;margin:0 0 .5rem;">{{WebUtility.HtmlEncode(heading)}}</h1>
            <p style="margin:0;color:#6b615c;">{{WebUtility.HtmlEncode(body)}}</p>
          </main>
        </body>
        </html>
        """;
}
