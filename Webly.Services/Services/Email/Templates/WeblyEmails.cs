namespace Webly.Services.Services.Email.Templates;

/// <summary>
/// Every message Webly sends. One method per email, each returning a ready-to-send
/// <see cref="EmailMessage"/> with both bodies, so they can be asserted in tests without a sender.
///
/// Product copy is English: Webly sells to whoever can type a sentence about their business, and a
/// single language everywhere is what keeps the mails, the app and the agent's replies consistent.
/// </summary>
public static class WeblyEmails
{
    public static EmailMessage VerifyEmail(string to, string displayName, string link, string code) =>
        new()
        {
            To = to,
            ToName = displayName,
            Subject = $"Your Webly confirmation code: {code}",
            HtmlBody = EmailLayout.Wrap(
                $"Hi {displayName},",
                $"Your confirmation code is {code}",
                EmailLayout.Paragraph("Thanks for signing up to Webly. Type this code into the page you left open:")
                + EmailLayout.Code(code)
                + EmailLayout.Small("Or confirm with one tap, if you are reading this on the same device:")
                + EmailLayout.Button(link, "Confirm email address")
                + EmailLayout.Small("The code and the link are valid for 24 hours.")
                + EmailLayout.FallbackLink(link),
                // One of the two messages that can land in front of somebody who did nothing: anybody can
                // type an address into a sign-up form. The footer has to give them the way out.
                footer: "Somebody signed up to Webly with this address. If it was not you, ignore this email."),
            TextBody =
                $"""
                Hi {displayName},

                Thanks for signing up to Webly. Your confirmation code is:

                {code}

                Type it into the page you left open, or confirm with this link:

                {link}

                The code and the link are valid for 24 hours. If you did not sign up, ignore this email.
                """
        };

    public static EmailMessage ResetPassword(string to, string displayName, string link) =>
        new()
        {
            To = to,
            ToName = displayName,
            Subject = "Reset your password",
            HtmlBody = EmailLayout.Wrap(
                "Reset your password",
                "Set a new password for your Webly account.",
                EmailLayout.Paragraph($"Hi {displayName}, you asked for a new password for your Webly account. Use the button below to set one.")
                + EmailLayout.Button(link, "Set a new password")
                + EmailLayout.Small("The link is valid for 1 hour and can only be used once.")
                + EmailLayout.Paragraph("If you did not ask for this, there is nothing to do — your password stays as it is.")
                + EmailLayout.FallbackLink(link)),
            TextBody =
                $"""
                Hi {displayName},

                You asked for a new password for your Webly account. Set one here:

                {link}

                The link is valid for 1 hour and can only be used once.
                If you did not ask for this, there is nothing to do — your password stays as it is.
                """
        };

    /// <summary>
    /// Sent after the password actually changes. Carries no link on purpose: its whole job is to let
    /// someone notice a change they did not make, and a "wasn't me" button in an email is exactly
    /// what a phishing message imitates.
    /// </summary>
    public static EmailMessage PasswordChanged(string to, string displayName) =>
        new()
        {
            To = to,
            ToName = displayName,
            Subject = "Your password changed",
            HtmlBody = EmailLayout.Wrap(
                "Your password changed",
                "A security notice about your Webly account.",
                EmailLayout.Paragraph($"Hi {displayName}, the password on your Webly account has just been changed, and every device has been signed out.")
                + EmailLayout.Paragraph("If that was you, there is nothing to do. If it was not, set a new password immediately from the sign-in page and check the security of your mailbox too.")),
            TextBody =
                $"""
                Hi {displayName},

                The password on your Webly account has just been changed, and every device has been
                signed out.

                If that was you, there is nothing to do. If it was not, set a new password immediately
                from the sign-in page and check the security of your mailbox too.
                """
        };

    /// <summary>
    /// Sent when a Google sign-in takes over an account whose address had never been verified. The
    /// person receiving it is the proven owner of the mailbox, so this is genuinely for them.
    /// </summary>
    public static EmailMessage PasswordRemoved(string to, string displayName) =>
        new()
        {
            To = to,
            ToName = displayName,
            Subject = "Your account now signs in with Google",
            HtmlBody = EmailLayout.Wrap(
                "Your account now signs in with Google",
                "A security notice about your Webly account.",
                EmailLayout.Paragraph($"Hi {displayName}, a Webly account was created with this email address some time ago, but the address was never confirmed.")
                + EmailLayout.Paragraph("You have now signed in with Google, which proves the address is yours. As a precaution the old password on the account has been removed and every earlier session closed.")
                + EmailLayout.Paragraph("From now on you sign in with Google. If you would also like a password, you can set one any time in your account settings.")),
            TextBody =
                $"""
                Hi {displayName},

                A Webly account was created with this email address some time ago, but the address was
                never confirmed. You have now signed in with Google, which proves the address is yours.

                As a precaution the old password on the account has been removed and every earlier
                session closed. From now on you sign in with Google; if you would also like a password,
                you can set one any time in your account settings.
                """
        };

    /// <summary>
    /// Sent when a deployment finishes. A deploy is the one thing in Webly that takes long enough for
    /// someone to close the tab, and the whole point of it is that the site is now live — so the
    /// result has to reach them somewhere other than a stream they may not be watching.
    /// </summary>
    public static EmailMessage SitePublished(string to, string displayName, string siteName, string url) =>
        new()
        {
            To = to,
            ToName = displayName,
            Subject = $"{siteName} is live",
            HtmlBody = EmailLayout.Wrap(
                $"{siteName} is live",
                "Your site has been published.",
                EmailLayout.Paragraph($"Hi {displayName}, the latest version of <strong>{siteName}</strong> has been published and is now serving visitors.")
                + EmailLayout.Button(url, "Open your site")
                + EmailLayout.Small("Every published version stays in your history, so you can roll back at any time.")
                + EmailLayout.FallbackLink(url)),
            TextBody =
                $"""
                Hi {displayName},

                The latest version of {siteName} has been published and is now serving visitors:

                {url}

                Every published version stays in your history, so you can roll back at any time.
                """
        };

    /// <summary>
    /// Sent when a deployment fails. Named as the deploy's own failure rather than a generic error:
    /// the previous version is still live, and saying so is the difference between an inconvenience
    /// and a panic.
    /// </summary>
    public static EmailMessage DeploymentFailed(
        string to,
        string displayName,
        string siteName,
        string reason,
        string? detail = null) =>
        new()
        {
            To = to,
            ToName = displayName,
            Subject = $"Publishing {siteName} failed",
            HtmlBody = EmailLayout.Wrap(
                $"Publishing {siteName} failed",
                "Your live site is unchanged.",
                EmailLayout.Paragraph($"Hi {displayName}, the latest attempt to publish <strong>{siteName}</strong> did not finish.")
                + EmailLayout.Paragraph(reason)
                // The reason's own words are "the error is below", and for a long time there was nothing
                // below: the detail stayed on the settings screen, which is the one place the person reading
                // this is not. It is the compiler's output, already made readable and trimmed by the caller.
                + (string.IsNullOrWhiteSpace(detail) ? string.Empty : EmailLayout.Log(detail))
                + EmailLayout.Paragraph("The version that was live before is still live and untouched. Open Webly and try publishing again — if it keeps failing, reply to this email.")),
            TextBody =
                $"""
                Hi {displayName},

                The latest attempt to publish {siteName} did not finish.

                {reason}
                {(string.IsNullOrWhiteSpace(detail) ? "" : "\n" + detail + "\n")}
                The version that was live before is still live and untouched. Open Webly and try
                publishing again — if it keeps failing, reply to this email.
                """
        };

    /// <summary>
    /// Sent when somebody fills in a form on a published site.
    ///
    /// <b>The only email here carrying words Webly did not write</b>, which is why every field goes through
    /// <see cref="EmailLayout.Escape"/> and why the subject line does not quote the visitor: a subject built
    /// from a stranger's first field is how a notification ends up looking like the spam it might be.
    ///
    /// <paramref name="replyTo"/> is the point of the whole message. A lead that has to be answered by copying
    /// an address out of an email body is a lead that waits a day; with the header set, the owner presses reply
    /// on their phone and the customer hears back.
    /// </summary>
    public static EmailMessage FormSubmitted(
        string to,
        string displayName,
        string siteName,
        IReadOnlyList<(string Name, string Value)> fields,
        string? replyTo,
        string link) =>
        new()
        {
            To = to,
            ToName = displayName,
            ReplyTo = replyTo,
            Subject = $"New message from your {siteName} website",
            HtmlBody = EmailLayout.Wrap(
                "You have a new message",
                $"Somebody filled in a form on {siteName}.",
                EmailLayout.Paragraph($"Hi {displayName}, somebody filled in a form on <strong>{EmailLayout.Escape(siteName)}</strong>.")
                + EmailLayout.Fields(fields)
                + (replyTo is null
                    ? EmailLayout.Small("They did not leave an email address, so check the message for another way to reach them.")
                    : EmailLayout.Small("Reply to this email and your answer goes straight to them."))
                + EmailLayout.Button(link, "See all your messages")),
            TextBody =
                $"""
                Hi {displayName},

                Somebody filled in a form on {siteName}.

                {string.Join("\n\n", fields.Select(field => $"{field.Name}:\n{field.Value}"))}

                {(replyTo is null
                    ? "They did not leave an email address, so check the message for another way to reach them."
                    : "Reply to this email and your answer goes straight to them.")}

                All your messages: {link}
                """
        };
}
