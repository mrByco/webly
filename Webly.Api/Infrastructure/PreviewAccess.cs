using Microsoft.AspNetCore.DataProtection;

namespace Webly.Api.Infrastructure;

/// <summary>
/// The credential the preview frame uses, which is not the session cookie — and the reason is the one thing
/// the preview's original design got backwards.
///
/// <b>Same-origin was treated as what made the preview safe.</b> It is also what made it dangerous. The frame
/// served the customer's own site, written by a coding agent, on Webly's origin: a script in that page could
/// call <c>/api/sites</c>, <c>/api/sites/x/submissions</c> or <c>DELETE /api/auth/account</c> with the owner's
/// session and be indistinguishable from the app doing it. The tokens are <c>HttpOnly</c>, so it could not read
/// them — it did not need to. Confirmed by putting a <c>fetch('/api/sites')</c> in a site's home page and
/// watching the preview print the owner's sites back.
///
/// The fix is to take the frame's origin away: <c>sandbox</c> without <c>allow-same-origin</c> gives the
/// document an opaque origin, and an opaque origin is not Webly's. What that costs is the session cookie —
/// everything the framed document then asks for is "cross-site" as far as the browser is concerned, so a
/// <c>SameSite=Lax</c> cookie is withheld and the proxy's per-request ownership check has nothing to check.
///
/// So the preview gets its own credential: a signed token naming one user and one site, in a cookie scoped to
/// <b>that site's preview path</b> and marked <c>SameSite=None</c> so it survives the opaque origin. What that
/// opens is narrow and worth stating: any page anywhere can now cause a browser to send it, so a third party
/// can make a customer's browser fetch that customer's own preview. They cannot read the answer — it is
/// cross-origin to them — and the path serves nothing but a proxy to the customer's own dev server.
///
/// It is not an authorization: <c>PreviewController</c> still calls <c>FindForOwnerLightAsync</c> with the user
/// it names, so a token for a site that has since been deleted, or transferred, grants exactly nothing.
///
/// <b>The stronger answer is a separate origin</b> — <c>{id}.preview.webly.site</c> — which needs a wildcard
/// record and a certificate this repository does not have yet. See <c>docs/agent-plan.md</c>.
/// </summary>
public class PreviewAccess(IDataProtectionProvider dataProtection)
{
    public const string CookieName = "webly_preview";

    /// <summary>
    /// Long enough for a working session, short enough that a token left on a shared machine expires. It is
    /// re-minted whenever the editor opens a site, so nobody meets the end of it mid-sentence.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Webly.Preview.v1");

    /// <summary>Where the cookie lives: one site's preview and nothing else on this origin.</summary>
    public static string PathFor(string siteNanoid) => $"/api/sites/{siteNanoid}/preview";

    public void Issue(HttpResponse response, string siteNanoid, int userId)
    {
        var expires = DateTimeOffset.UtcNow.Add(Lifetime);
        var token = _protector.Protect($"{userId}|{siteNanoid}|{expires.ToUnixTimeSeconds()}");

        response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,

            // Required with SameSite=None, and true here anyway: the app is HTTPS in development as well as
            // production. A browser silently drops this cookie on plain HTTP, which is the one way this fails
            // quietly — so the editor asks for it again on every load rather than assuming it stuck.
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = PathFor(siteNanoid),
            Expires = expires
        });
    }

    /// <summary>
    /// The user this request may preview <paramref name="siteNanoid"/> as, or null.
    ///
    /// Everything is checked: that the token unprotects (so it is ours and unmodified), that it names this
    /// site, and that it has not expired. A token for another site is not a near miss to be tolerated — the
    /// cookie is path-scoped, so one arriving here at all means something is wrong.
    /// </summary>
    public int? UserFor(HttpRequest request, string siteNanoid)
    {
        var cookie = request.Cookies[CookieName];

        if (string.IsNullOrEmpty(cookie)) return null;

        string plain;

        try
        {
            plain = _protector.Unprotect(cookie);
        }
        catch (Exception)
        {
            // Forged, or minted before a key rotation. Either way it is not a credential.
            return null;
        }

        var parts = plain.Split('|');

        if (parts.Length != 3) return null;
        if (!string.Equals(parts[1], siteNanoid, StringComparison.Ordinal)) return null;
        if (!int.TryParse(parts[0], out var userId)) return null;
        if (!long.TryParse(parts[2], out var expiry)) return null;
        if (DateTimeOffset.FromUnixTimeSeconds(expiry) <= DateTimeOffset.UtcNow) return null;

        return userId;
    }
}
