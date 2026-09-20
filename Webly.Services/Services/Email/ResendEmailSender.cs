using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Webly.Services.Services.Email;

/// <summary>
/// Sends through Resend's HTTP API. Registered only when an API key is configured — without one the
/// app falls back to <see cref="LoggingEmailSender"/>, so development and CI need no credentials.
/// </summary>
public class ResendEmailSender(
    HttpClient httpClient,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            from = $"{_options.FromName} <{_options.FromAddress}>",
            to = new[] { message.To },
            subject = message.Subject,
            html = message.HtmlBody,
            text = message.TextBody
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Resend.ApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
            return;

        // Logged with the body — Resend explains refusals (unverified sending domain, invalid
        // recipient) in it, and without that a failed send is just a status code.
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        logger.LogError(
            "Resend refused a message to {Recipient} with {StatusCode}: {Body}",
            message.To,
            response.StatusCode,
            body);

        throw new InvalidOperationException($"Sending email failed with status {(int)response.StatusCode}.");
    }
}
