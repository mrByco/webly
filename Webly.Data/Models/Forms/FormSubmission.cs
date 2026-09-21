using System.ComponentModel.DataAnnotations;
using Webly.Data.Models.Interfaces;
using Webly.Data.Models.Sites;

namespace Webly.Data.Models.Forms;

/// <summary>
/// One thing a visitor sent from a published site — a contact form, a quote request, a booking enquiry.
///
/// <b>This is the product's only inbound path, and the only row a stranger can create.</b> Everything else
/// in Webly is written by a signed-in owner or by the app on their behalf; this is written by whoever loaded
/// the page. So the endpoint that creates it is the one place that assumes nothing about its caller: a
/// honeypot, a rate limit, a cap per site, a bound on how much can arrive, and nothing stored that was not
/// sent.
///
/// <b>The site has no server of its own.</b> A published site is a static export, so there is nowhere in it
/// for a form to post to — which is what decides the question <c>MASTER_PLAN.md</c> P4 left open. The form
/// posts to Webly, as an ordinary HTML form, and Webly redirects the visitor back. That also means no CORS
/// and no JavaScript: a form post is not a cross-origin request a browser needs permission for, and a
/// contact form that stops working because a script did not load is the worst possible thing to be fragile.
/// </summary>
public class FormSubmission : IHasNanoid, IHasCreatedAt
{
    [Key]
    public int Id { get; set; }

    public string Nanoid { get; set; } = string.Empty;

    /// <summary>When the visitor sent it. Never updated: see <see cref="IHasCreatedAt"/>.</summary>
    public DateTime CreatedAt { get; set; }

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    /// <summary>
    /// Which form on the site it came from, as the form itself declared in a hidden field. Free text rather
    /// than a catalogue, because the agent writes the pages: a second form for quotes is one more page, not a
    /// migration. Defaulted rather than required, so a form that forgets the field still works.
    /// </summary>
    [MaxLength(64)]
    public string FormName { get; set; } = DefaultFormName;

    public const string DefaultFormName = "contact";

    /// <summary>
    /// What was filled in, in the order the form sent it, as the visitor's own labels and values.
    ///
    /// <b>Stored as submitted, not as a shape we defined.</b> The agent writes the form, so Webly cannot know
    /// whether this site asks for a phone number or a postcode — and a fixed set of columns would mean every
    /// new question on a form needed a migration, which is the opposite of what "the agent writes the pages"
    /// buys. One jsonb column, read by a screen that lists pairs.
    /// </summary>
    public List<SubmittedField> Fields { get; set; } = [];

    /// <summary>
    /// Where it came from, for the case somebody has to be blocked or a flood explained. Nullable because a
    /// deployment behind a proxy that does not forward it honestly has no answer, and inventing one would be
    /// worse than admitting it.
    /// </summary>
    [MaxLength(64)]
    public string? SubmittedFromIp { get; set; }
}

/// <summary>One field of a submission. A name and a value, both the visitor's.</summary>
public class SubmittedField
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
