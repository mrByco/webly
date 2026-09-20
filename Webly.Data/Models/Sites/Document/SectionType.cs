namespace Webly.Data.Models.Sites.Document;

/// <summary>
/// The closed set of things a page can be built from. Closed is the point: a person can ask for
/// anything, and what they get is assembled from a catalogue that is known to render, to be editable
/// by hand afterwards, and to be responsive — which is what "without touching a single line of code"
/// actually requires. An agent that could invent a section type would be an agent that can produce a
/// page nobody can edit and nothing can validate.
///
/// Adding a value is four edits in one place each: this enum, the schema in <c>SectionCatalogue</c>,
/// a template in the renderer, and a preview thumbnail. It is a deliberate, reviewable act.
///
/// Persisted and transmitted as a name, never a number — see <c>Program.cs</c>.
/// </summary>
public enum SectionType
{
    /// <summary>Headline, subheadline, one or two calls to action, optional image.</summary>
    Hero,

    /// <summary>A prose block. The one section whose body is rich text rather than fields.</summary>
    RichText,

    /// <summary>A grid of icon/title/description items. "What we do".</summary>
    FeatureGrid,

    /// <summary>Quotes with an author, a role and an optional photo.</summary>
    Testimonials,

    /// <summary>Question and answer pairs, rendered as a disclosure list and as FAQ structured data.</summary>
    Faq,

    /// <summary>One sentence and one button, for the bottom of a page.</summary>
    Cta

    // Designed and deliberately not here yet, in the order they are worth adding: Gallery, Pricing,
    // LogoCloud, Stats, Steps, Team, LocationMap, ContactForm. Each is the four edits above and
    // nothing else — the catalogue is the extension point, which is the reason it is shaped this way.
    // ContactForm is last on purpose: a form needs somewhere for submissions to go, and that is a
    // slice of its own (see MASTER_PLAN.md), not a section type.
}
