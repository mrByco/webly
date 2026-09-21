using System.ComponentModel.DataAnnotations;

namespace Webly.Services.Services;

/// <summary>
/// Where this deployment answers on the internet.
///
/// One setting for one fact, and it has three users that must agree. Links in mail have to point at a
/// real Webly (<c>/verify-email?token=…</c>); the editor is served from the same origin, because the
/// browser only ever talks to one; and a <b>published site's contact form posts back to it</b> — that
/// one is the reason this class exists rather than a second <c>BaseUrl</c> beside the email options,
/// since a form action and a verification link are the same sentence about the same host and two
/// settings for it is two things that can disagree.
///
/// <b>Configuration, never the inbound request's host.</b> An attacker who can set <c>Host</c> would
/// otherwise get password-reset links pointed at their own domain and mailed out by us — and, now,
/// every published form pointed somewhere else as well.
/// </summary>
public class AppOptions
{
    public const string SectionName = "App";

    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The origin with any trailing slash gone, which is what every caller actually wants.</summary>
    public string Origin => BaseUrl.TrimEnd('/');

    /// <summary>
    /// Where this site's forms post, as an absolute URL baked into the published pages.
    ///
    /// Here rather than at either call site, because there are two and they must not disagree: the dev server
    /// gets it so the preview's form works, and the publish gets it so the live one does. A preview that posts
    /// somewhere else than the published page is the kind of difference somebody finds after launch.
    /// </summary>
    public string FormEndpointFor(string siteNanoid) => $"{Origin}/api/public/forms/{siteNanoid}";
}
