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
                + EmailLayout.Paragraph("<span style=\"font-size:13px;color:#6b615c;\">Or confirm with one tap, if you are reading this on the same device:</span>")
                + EmailLayout.Button(link, "Confirm email address")
                + EmailLayout.Paragraph("<span style=\"font-size:13px;color:#6b615c;\">The code and the link are valid for 24 hours.</span>")
                + EmailLayout.FallbackLink(link)),
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
                + EmailLayout.Paragraph("<span style=\"font-size:13px;color:#6b615c;\">The link is valid for 1 hour and can only be used once.</span>")
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
                + EmailLayout.Paragraph("<span style=\"font-size:13px;color:#6b615c;\">Every published version stays in your history, so you can roll back at any time.</span>")
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
    public static EmailMessage DeploymentFailed(string to, string displayName, string siteName, string reason) =>
        new()
        {
            To = to,
            ToName = displayName,
            Subject = $"Publishing {siteName} failed",
            HtmlBody = EmailLayout.Wrap(
                $"Publishing {siteName} failed",
                "Your live site is unchanged.",
                EmailLayout.Paragraph($"Hi {displayName}, the latest attempt to publish <strong>{siteName}</strong> did not finish.")
                + EmailLayout.Paragraph($"<span style=\"font-size:13px;color:#6b615c;\">{reason}</span>")
                + EmailLayout.Paragraph("The version that was live before is still live and untouched. Open Webly and try publishing again — if it keeps failing, reply to this email.")),
            TextBody =
                $"""
                Hi {displayName},

                The latest attempt to publish {siteName} did not finish.

                {reason}

                The version that was live before is still live and untouched. Open Webly and try
                publishing again — if it keeps failing, reply to this email.
                """
        };
}
