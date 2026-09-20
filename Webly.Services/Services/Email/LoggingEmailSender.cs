using Microsoft.Extensions.Logging;

namespace Webly.Services.Services.Email;

/// <summary>
/// The development sender: writes the message to the log instead of sending it.
///
/// It also drops the HTML into <c>.run/mail/</c> (gitignored), because a console is not a renderer —
/// reading email markup as source tells you nothing about whether the layout survived, and a broken
/// template that nobody looked at is the normal way transactional mail ends up ugly in production.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var savedTo = await TrySaveAsync(message, cancellationToken);

        logger.LogInformation(
            "Email not sent (no provider configured).\n  To: {Recipient}\n  Subject: {Subject}\n  Saved: {SavedTo}\n{HtmlBody}",
            message.To,
            message.Subject,
            savedTo ?? "(could not write file)",
            message.HtmlBody);
    }

    private static async Task<string?> TrySaveAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".run", "mail");
            Directory.CreateDirectory(directory);

            var name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Sanitize(message.Subject)}.html";
            var path = Path.GetFullPath(Path.Combine(directory, name));

            await File.WriteAllTextAsync(path, message.HtmlBody, cancellationToken);

            return path;
        }
        catch (Exception)
        {
            // A convenience, not the feature. Failing to write the file must never fail the flow
            // that was trying to send an email.
            return null;
        }
    }

    private static string Sanitize(string subject)
    {
        var cleaned = new string(subject.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray());

        return cleaned.Trim().Replace(' ', '-').ToLowerInvariant() is { Length: > 0 } slug
            ? slug[..Math.Min(slug.Length, 40)]
            : "email";
    }
}
