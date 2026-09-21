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

    /// <summary>
    /// Who a reply should go to, when that is not us.
    ///
    /// It exists for one message and earns its place there: a form submission's notification is the start of a
    /// conversation with a customer, and an owner who has to copy an address out of the body before answering
    /// will lose some of those. Null everywhere else, because replying to a verification email should reach
    /// nobody.
    /// </summary>
    public string? ReplyTo { get; init; }
}
