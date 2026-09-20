using Webly.Services.Services.Email;
using System.Text.RegularExpressions;

namespace Webly.Tests;

/// <summary>
/// Captures what would have been sent, so a test can assert on the mail and pull the token straight
/// out of the link — which is also the closest thing to what a real user does.
/// </summary>
public partial class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public EmailMessage Last => Sent.Count > 0
        ? Sent[^1]
        : throw new InvalidOperationException("No email was sent.");

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add(message);

        return Task.CompletedTask;
    }

    /// <summary>Pulls the `token` query value out of the most recent email's link.</summary>
    public string TokenFromLastLink() => TokenFromLink(Last);

    public static string TokenFromLink(EmailMessage message)
    {
        var match = TokenPattern().Match(message.TextBody);

        return match.Success
            ? Uri.UnescapeDataString(match.Groups[1].Value)
            : throw new InvalidOperationException($"No token link in:\n{message.TextBody}");
    }

    [GeneratedRegex(@"[?&]token=([^\s&]+)")]
    private static partial Regex TokenPattern();
}
