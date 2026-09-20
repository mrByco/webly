namespace Webly.Services.Services.Sites;

/// <summary>
/// What kind of value a section field holds. Coarse on purpose: these are the distinctions that
/// change how the field is <i>edited</i> (a colour picker, an image picker, a textarea) and how it is
/// validated, not a type system. A finer set would mean more branches in the property editor for no
/// user-visible difference.
/// </summary>
public enum SectionFieldKind
{
    /// <summary>One line. Rendered as text, escaped.</summary>
    Text,

    /// <summary>Several paragraphs. A restricted subset of HTML, sanitized on write.</summary>
    RichText,

    /// <summary>An absolute URL, or an internal <c>page:{pageId}</c> reference.</summary>
    Link,

    /// <summary>
    /// An image. In this slice the value is an absolute https URL — the customer's existing site, a
    /// stock library, a CDN — because a self-service editor that cannot show a picture until uploads
    /// exist is not demonstrable, and the agent can put a real photograph on a page by URL today.
    /// When the asset store lands (MASTER_PLAN.md P5) the field additionally accepts a blob name and
    /// the renderer's resolver tells the two apart; that is why the renderer resolves every image
    /// value through one method instead of writing the string into the markup.
    /// </summary>
    Image,

    Color,
    Number,
    Boolean,

    /// <summary>One of <see cref="SectionFieldSchema.Choices"/>.</summary>
    Choice,

    /// <summary>
    /// A repeating group, whose shape is <see cref="SectionFieldSchema.ItemFields"/>. One level deep and
    /// no deeper: a nested list inside a list is a layout the section catalogue should express as its own
    /// section type, and allowing it would make both the editor and the validator recursive for no case
    /// anybody has asked for.
    /// </summary>
    List
}

/// <summary>
/// One field of one section type. This record is read by four things, which is the entire reason the
/// catalogue exists: the validator that rejects a bad write, the agent's tool description (so the model
/// knows what it may set without being told separately), the client's property editor, and the
/// renderer's template. Adding a field is one line here.
/// </summary>
public record SectionFieldSchema(
    string Name,
    SectionFieldKind Kind,

    /// <summary>
    /// Written for the model as much as for the person: this sentence is what the agent reads to decide
    /// what belongs in the field, so "the one promise the visitor should remember" beats "subtitle".
    /// </summary>
    string Description,

    bool Required = false,
    int? MaxLength = null,
    IReadOnlyList<string>? Choices = null,
    IReadOnlyList<SectionFieldSchema>? ItemFields = null,
    int? MaxItems = null);
