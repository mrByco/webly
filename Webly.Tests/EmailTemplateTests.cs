using Webly.Services.Services.Email;
using Webly.Services.Services.Email.Templates;

namespace Webly.Tests;

/// <summary>
/// Every message Webly sends, checked for the things that go wrong quietly.
///
/// These templates were ported from the reference project and nobody opened one for months, so every Webly
/// email went out in that project's paprika header, with a bowl-of-stew emoji beside the name and
/// <c>lang="hu"</c> on the document. Nothing failed; the product simply sent mail branded as another app. An
/// email is the one part of this product a customer sees when they are not looking at it, and it is the part
/// with no screen to notice on.
///
/// So this is a structural check rather than a snapshot: a snapshot of hand-written email HTML fails on every
/// wording change and gets regenerated without being read, which is the same as not having it.
/// </summary>
public class EmailTemplateTests
{
    private static IEnumerable<(string Name, EmailMessage Message)> All()
    {
        yield return ("verify", WeblyEmails.VerifyEmail("a@example.com", "Ada", "https://webly.test/verify?t=x", "470268"));
        yield return ("reset", WeblyEmails.ResetPassword("a@example.com", "Ada", "https://webly.test/reset?t=x"));
        yield return ("password changed", WeblyEmails.PasswordChanged("a@example.com", "Ada"));
        yield return ("password removed", WeblyEmails.PasswordRemoved("a@example.com", "Ada"));
        yield return ("published", WeblyEmails.SitePublished("a@example.com", "Ada", "Ridgeway Cycles", "https://ridgeway.test/"));
        yield return ("failed", WeblyEmails.DeploymentFailed(
            "a@example.com", "Ada", "Ridgeway Cycles", "Your site did not build.", "./src/app/page.tsx\n  Unexpected token"));
        yield return ("form", WeblyEmails.FormSubmitted(
            "a@example.com", "Ada", "Ridgeway Cycles",
            [("Name", "Priya"), ("Message", "Do you fit mudguards?")], "priya@example.com", "https://webly.test/messages"));
    }

    [Test]
    public void Every_message_is_Webly_is_rather_than_the_project_it_was_ported_from()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, message) in All())
            {
                Assert.That(message.HtmlBody, Does.Contain("lang=\"en\""), name);

                // The reference project's palette and its emoji, each by value. A comment saying "do not put
                // the paprika back" is not something a build can enforce; this is.
                Assert.That(message.HtmlBody, Does.Not.Contain("#c2410c"), $"{name}: the reference project's brand colour");
                Assert.That(message.HtmlBody, Does.Not.Contain("#6b615c"), $"{name}: the reference project's muted colour");
                Assert.That(message.HtmlBody, Does.Not.Contain("#faf7f2"), $"{name}: the reference project's background");
                Assert.That(message.HtmlBody, Does.Not.Contain("\U0001F372"), $"{name}: a bowl of stew");

                // And it is Webly's colour that replaced them, in the header and on the buttons.
                Assert.That(message.HtmlBody, Does.Contain("#3b5bd4"), name);
            }
        });
    }

    [Test]
    public void Every_message_has_both_bodies_and_says_who_it_is_from()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, message) in All())
            {
                Assert.That(message.Subject, Is.Not.Empty, name);

                // A plain-text part on every one: a mail with only HTML scores as spam and is unreadable in
                // the clients that refuse to render it.
                Assert.That(message.TextBody?.Trim(), Is.Not.Empty, name);
                Assert.That(message.HtmlBody, Does.Contain("Sent by Webly").Or.Contain("signed up to Webly"), name);
            }
        });
    }

    /// <summary>
    /// The failure mail promised the error and did not carry it: "the error is below" with nothing below,
    /// because the build log stayed on the settings screen — which is the one place the person reading the
    /// mail is not.
    /// </summary>
    [Test]
    public void A_failed_publish_carries_the_build_log()
    {
        var message = WeblyEmails.DeploymentFailed(
            "a@example.com", "Ada", "Ridgeway Cycles", "Your site did not build.", "./src/app/page.tsx\n  Unexpected token");

        Assert.Multiple(() =>
        {
            Assert.That(message.HtmlBody, Does.Contain("Unexpected token"));
            Assert.That(message.TextBody, Does.Contain("Unexpected token"));

            // And it still reads correctly when there is nothing to show — a sandbox that never started has
            // no compiler output, and an empty grey box is worse than no box.
            var bare = WeblyEmails.DeploymentFailed("a@example.com", "Ada", "Ridgeway Cycles", "No machine was free.");

            Assert.That(bare.HtmlBody, Does.Not.Contain("<pre"));
        });
    }

    /// <summary>
    /// The one message built from a stranger's words. Its fields go through the escaper, and its subject
    /// deliberately does not quote them — a subject line written by whoever filled in the form is how a
    /// notification ends up looking like the spam it might be.
    /// </summary>
    [Test]
    public void A_form_notification_cannot_carry_markup_from_the_visitor()
    {
        var message = WeblyEmails.FormSubmitted(
            "a@example.com", "Ada", "Ridgeway Cycles",
            [("Message", "<script>alert(1)</script> & \"quoted\"")], replyTo: null, link: "https://webly.test/messages");

        Assert.Multiple(() =>
        {
            Assert.That(message.HtmlBody, Does.Not.Contain("<script>"));
            Assert.That(message.HtmlBody, Does.Contain("&lt;script&gt;"));
            Assert.That(message.HtmlBody, Does.Contain("&amp;"));
            Assert.That(message.Subject, Does.Not.Contain("script"));

            // No reply-to when there was no address to use, rather than a header pointing at nothing.
            Assert.That(message.ReplyTo, Is.Null);
        });
    }

    /// <summary>
    /// No message carries our own notes to the customer.
    ///
    /// An HTML comment renders as nothing, which is exactly why this needed a test rather than a reading: three
    /// paragraphs explaining a header colour, a dark-mode meta tag and a footer decision went out in every Webly
    /// email for as long as they existed, and nobody saw them because no client draws a comment. They are
    /// engineering commentary about another project, sitting in a stranger's inbox behind "view source".
    ///
    /// Every template rather than the layout, because the layout is only where they were this time.
    /// </summary>
    [Test]
    public void No_message_carries_an_html_comment()
    {
        var messages = new[]
        {
            WeblyEmails.VerifyEmail("a@example.com", "Ada", "https://webly.test/verify", "123456"),
            WeblyEmails.ResetPassword("a@example.com", "Ada", "https://webly.test/reset"),
            WeblyEmails.PasswordChanged("a@example.com", "Ada"),
            WeblyEmails.PasswordRemoved("a@example.com", "Ada"),
            WeblyEmails.SitePublished("a@example.com", "Ada", "Ridgeway Cycles", "https://ridgeway.example"),
            WeblyEmails.DeploymentFailed("a@example.com", "Ada", "Ridgeway Cycles", "It did not compile."),
            WeblyEmails.FormSubmitted(
                "a@example.com", "Ada", "Ridgeway Cycles", [("Message", "Hello")],
                replyTo: null, link: "https://webly.test/messages"),
        };

        Assert.Multiple(() =>
        {
            foreach (var message in messages)
            {
                // `<!doctype` is the one thing that legitimately opens with those characters.
                Assert.That(message.HtmlBody, Does.Not.Contain("<!--"), message.Subject);
                Assert.That(message.TextBody, Does.Not.Contain("<!--"), message.Subject);
            }
        });
    }
}
