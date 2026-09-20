namespace Webly.Services.Services.Email;

/// <summary>
/// One outgoing message. Always both bodies: some clients prefer the text part, and having one
/// measurably helps a transactional mail reach the inbox rather than the spam folder.
/// </summary>
public record EmailMessage
{
    public required string To { get; init; }
    public string? ToName { get; init; }
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public required string TextBody { get; init; }
}
