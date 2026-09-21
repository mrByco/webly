namespace Webly.Services.Services.Email.Templates;

/// <summary>
/// The shared shell every Webly email is poured into.
///
/// Tables and inline styles, not flexbox and a stylesheet — Outlook still renders with Word's engine
/// and Gmail strips most of what a browser would honour, so email markup is its own dialect. Written
/// by hand rather than through a template engine because there are four short messages here and a
/// dependency would be more code than the templates it renders.
/// </summary>
public static class EmailLayout
{
    private const string Paprika = "#c2410c";
    private const string Ink = "#2b2320";
    private const string Muted = "#6b615c";
    private const string Paper = "#faf7f2";

    /// <param name="preheader">
    /// The grey line inboxes show after the subject. Hidden in the body itself — left unset, clients
    /// fill it with whatever text comes first, which is usually the logo alt text.
    /// </param>
    public static string Wrap(string heading, string preheader, string bodyHtml) =>
        $"""
        <!doctype html>
        <html lang="hu">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{heading}</title>
        </head>
        <body style="margin:0;padding:0;background:{Paper};">
          <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{preheader}</div>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Paper};padding:32px 16px;">
            <tr>
              <td align="center">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #ece5dc;">
                  <tr>
                    <td style="background:{Paprika};padding:20px 28px;">
                      <span style="font:600 20px/1.2 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#ffffff;">
                        &#127858; Webly
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
                    <td style="padding:0 28px 28px;font:400 13px/1.5 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Muted};border-top:1px solid #f0eae2;padding-top:20px;">
                      Sent by Webly. If you did not ask for it, you can ignore it.
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

    public static string Button(string url, string label) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" style="margin:24px 0;">
          <tr>
            <td style="border-radius:10px;background:{Paprika};">
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
            <td style="border-radius:12px;background:{Paper};border:1px solid #ece5dc;padding:16px 24px;">
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
        <p style="margin:0;font-size:13px;word-break:break-all;"><a href="{url}" style="color:{Paprika};">{url}</a></p>
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
