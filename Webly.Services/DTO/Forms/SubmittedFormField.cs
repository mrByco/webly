namespace Webly.Services.DTO.Forms;

/// <summary>One field as it arrived from the browser, before anything has decided what it means.</summary>
public record SubmittedFormField(string Name, string Value);

/// <summary>
/// The field names the form itself uses to talk to Webly, rather than to the person reading the message.
///
/// One underscore-prefixed name each, in one place, because three things have to agree about them: the form
/// in the site's own source (which the agent writes, so <c>AGENTS.md</c> names them too), the endpoint that
/// reads them, and the screen that must not show them as if a visitor had typed them.
/// </summary>
public static class FormFieldNames
{
    /// <summary>Which form this is, so a quote request and a contact form are told apart in the editor.</summary>
    public const string Form = "_form";

    /// <summary>
    /// The honeypot. A field a person never sees and never fills in, so anything in it means the sender was
    /// not a person. The submission is dropped and the answer is a success, deliberately: telling a bot it
    /// was caught is telling it what to change.
    /// </summary>
    public const string Honeypot = "_ignore";

    /// <summary>
    /// Where to send the visitor afterwards, as a path relative to the page the form was on — so the site
    /// gets to show its own "thanks, we will be in touch" rather than Webly showing a bare one.
    /// </summary>
    public const string Next = "_next";

    /// <summary>Whether a name is one of ours, and therefore not something a visitor filled in.</summary>
    public static bool IsReserved(string name) => name.StartsWith('_');
}
