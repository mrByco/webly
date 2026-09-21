namespace Webly.Services.Services.Email.Templates;

/// <summary>
/// The shared shell every Webly email is poured into.
///
/// Tables and inline styles, not flexbox and a stylesheet — Outlook still renders with Word's engine
/// and Gmail strips most of what a browser would honour, so email markup is its own dialect. Written
/// by hand rather than through a template engine because there are seven short messages here and a
/// dependency would be more code than the templates it renders.
///
/// <b>The colours are the app's own, as hex.</b> They were the reference project's for a while and nobody
/// noticed, because nobody opened one: every Webly email went out in a paprika header with a bowl-of-stew
/// emoji beside the name and <c>lang="hu"</c> on the document. An email is the only part of this product a
/// customer sees when they are not looking at it, and it was branded as another app. Email cannot read a CSS
/// variable, so these are the same numbers `client/src/styles.css` derives its palette from, converted once
/// and written down — change one there and change it here.
/// </summary>
public static class EmailLayout
{
    /// <summary>The app's primary, <c>oklch(52% 0.19 268)</c>.</summary>
    private const string Brand = "#3b5bd4";

    private const string Ink = "#21242a";
    private const string Muted = "#656970";
    private const string Paper = "#f2f3f7";
    private const string Edge = "#dcdee2";

    /// <summary>
    /// What most of these messages are: something the account asked for by using Webly. The two that are not —
    /// a sign-up code and a password reset, which can both be sent to somebody who did not ask — pass their
    /// own.
    /// </summary>
    public const string DefaultFooter = "Sent by Webly because of something on your account.";

    /// <param name="preheader">
    /// The grey line inboxes show after the subject. Hidden in the body itself — left unset, clients
    /// fill it with whatever text comes first, which is usually the logo alt text.
    /// </param>
    /// <param name="footer">
    /// The small line under the rule. It belongs to the message: a confirmation code can honestly say "if you
    /// did not ask for this, ignore it", and a notice about somebody's own website cannot.
    /// </param>
    /// <remarks>
    /// Three things in the markup below are decisions rather than boilerplate, and they are explained here
    /// rather than in the HTML because <b>an HTML comment in a template is an HTML comment in the message</b>.
    /// It renders as nothing, so nobody sees it — and it travels to every customer's inbox all the same, where
    /// "view source" shows them our notes about another project. Which is exactly how three paragraphs of
    /// engineering commentary shipped in every Webly email until somebody read one as text.
    ///
    /// <list type="bullet">
    /// <item><c>color-scheme</c>: clients that honour it stop inverting the card into something nobody
    /// chose.</item>
    /// <item>The header is the name in the app's own colour and <b>no emoji</b>. A picture of food was the
    /// reference project's, and a glyph in a header renders differently in every client anyway.</item>
    /// <item>The footer belongs to the message, which is what the <paramref name="footer"/> parameter is
    /// for — see its own note.</item>
    /// </list>
    /// </remarks>
    public static string Wrap(string heading, string preheader, string bodyHtml, string footer = DefaultFooter) =>
        $"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{heading}</title>
          <meta name="color-scheme" content="light">
          <meta name="supported-color-schemes" content="light">
        </head>
        <body style="margin:0;padding:0;background:{Paper};">
          <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{preheader}</div>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Paper};padding:32px 16px;">
            <tr>
              <td align="center">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid {Edge};">
                  <tr>
                    <td style="background:{Brand};padding:20px 28px;">
                      <span style="font:600 20px/1.2 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#ffffff;letter-spacing:-0.01em;">
                        Webly
                      </span>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:32px 28px;font:400 16px/1.6 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Ink};">
                      <h1 style="margin:0 0 16px;font:600 24px/1.3 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Ink};">{heading}</h1>
                      {bodyHtml}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:0 28px 28px;font:400 13px/1.5 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Muted};border-top:1px solid {Edge};padding-top:20px;">
                      {footer}
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;

    public static string Paragraph(string text) =>
        $"""<p style="margin:0 0 16px;">{text}</p>""";

    /// <summary>
    /// A quieter paragraph — an aside, a validity window, a reassurance.
    ///
    /// It exists because every template had the muted colour written into it as a literal hex, six copies of
    /// one number that the palette above is supposed to own. Three of them were still the reference project's
    /// brown after the rest of the shell had been repainted.
    /// </summary>
    public static string Small(string text) =>
        $"""<p style="margin:0 0 16px;font-size:13px;color:{Muted};">{text}</p>""";

    public static string Button(string url, string label) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" style="margin:24px 0;">
          <tr>
            <td style="border-radius:10px;background:{Brand};">
              <a href="{url}" style="display:inline-block;padding:12px 24px;font:600 16px/1 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#ffffff;text-decoration:none;border-radius:10px;">{label}</a>
            </td>
          </tr>
        </table>
        """;

    /// <summary>
    /// A code meant to be read off one screen and typed into another, so it is set large, spaced and
    /// monospaced. Letter-spacing on the last character would push it off centre, hence the padding
    /// that compensates for it.
    /// </summary>
    public static string Code(string code) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" style="margin:8px 0 24px;">
          <tr>
            <td style="border-radius:12px;background:{Paper};border:1px solid {Edge};padding:16px 24px;">
              <span style="font:700 30px/1 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;letter-spacing:8px;padding-left:8px;color:{Ink};">{code}</span>
            </td>
          </tr>
        </table>
        """;

    /// <summary>
    /// The link in plain text under the button. Some clients strip or mangle anchors, and a user who
    /// cannot click needs something to copy.
    /// </summary>
    public static string FallbackLink(string url) =>
        $"""
        <p style="margin:0 0 8px;font-size:13px;color:{Muted};">If the button does not work, paste this address into your browser:</p>
        <p style="margin:0;font-size:13px;word-break:break-all;"><a href="{url}" style="color:{Brand};">{url}</a></p>
        """;

    /// <summary>
    /// Output from a build, as something somebody can read in an inbox.
    ///
    /// <b>The email that needed it was promising it and not showing it.</b> "Your site did not build — the
    /// error is below" is true on the settings screen, where the log is folded underneath, and it was simply
    /// false in the mail: the sentence went out with nothing after it. The whole reason to mail a failure is
    /// that the person is not looking at the app, so the words they need have to travel with it.
    ///
    /// Monospace, escaped, and left to scroll rather than wrap: a compiler's column markers mean nothing once
    /// a line has been folded. It is the caller's job to trim it — see <c>CompilerOutput</c>.
    /// </summary>
    public static string Log(string text) =>
        $"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:8px 0 24px;">
          <tr>
            <td style="border-radius:12px;background:{Paper};border:1px solid {Edge};padding:14px 16px;">
              <pre style="margin:0;overflow-x:auto;font:400 12px/1.5 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;color:{Ink};white-space:pre;">{Escape(text)}</pre>
            </td>
          </tr>
        </table>
        """;

    /// <summary>
    /// Text that came from outside, as something safe to put in a message body.
    ///
    /// <b>The first template that needed it is the one a stranger writes.</b> Every other email here is built
    /// from a name and a link this app already owns; a form submission is a visitor's own words, and an
    /// unescaped <c>&lt;</c> in them is at best a broken layout in somebody's inbox and at worst markup the
    /// owner's mail client renders. Ampersand first, or the escaping escapes itself.
    /// </summary>
    public static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    /// <summary>
    /// A submission's fields, as a table of labels and what was typed under them.
    ///
    /// A table rather than paragraphs because the labels are the form's own — "How can we help?" is a field
    /// name here — and a message whose questions and answers run together is one the owner has to decode. The
    /// value keeps its line breaks: somebody typed a paragraph into a textarea and collapsing it loses what
    /// they meant.
    /// </summary>
    public static string Fields(IReadOnlyList<(string Name, string Value)> fields) =>
        $"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:8px 0 24px;">
          {string.Concat(fields.Select(field => $"""
          <tr>
            <td style="padding:0 0 4px;font:600 13px/1.4 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Muted};">{Escape(field.Name)}</td>
          </tr>
          <tr>
            <td style="padding:0 0 16px;font:400 15px/1.5 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Ink};white-space:pre-wrap;">{Escape(field.Value)}</td>
          </tr>
          """))}
        </table>
        """;
}
