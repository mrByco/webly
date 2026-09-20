using System.Text;
using System.Text.Json.Nodes;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Sites;

/// <summary>
/// The declared shape of every section type, in one place.
///
/// This is Webly's single registration point, and the decision the product rests on. A section's props
/// are validated against these schemas on every write, so a document that is in the database renders;
/// the agent's tool description is generated from them, so the model cannot invent a field; the
/// client's property editor is generated from them, so a section the agent wrote is editable by hand
/// the moment it exists; and the renderer's template for a type can read its fields without defending
/// against their absence.
///
/// The reference project (auto-grader) has the same problem and solved it with a typed class per block
/// type — and records in its own notes that a block type then spans <i>five</i> registration points,
/// where missing one is a blank space on a page. One schema plus one template is two, and the second is
/// the part that genuinely has to be written by hand.
/// </summary>
public static class SectionCatalogue
{
    public static IReadOnlyList<SectionSchema> All { get; } =
    [
        new(SectionType.Hero, "Hero",
            "The first thing a visitor sees: what this is and why they should care. Every site has exactly one, at the top of the home page.",
            [
                new("headline", SectionFieldKind.Text, "The one thing the visitor must understand. Six to twelve words.", Required: true, MaxLength: 120),
                new("subheadline", SectionFieldKind.Text, "Who it is for and what it does for them. One sentence.", MaxLength: 240),
                new("image", SectionFieldKind.Image, "A photograph of the actual thing — the shop, the product, the person. Not an abstract illustration."),
                new("layout", SectionFieldKind.Choice, "Where the image sits relative to the text.", Choices: ["imageRight", "imageLeft", "imageBackground", "textOnly"]),
                new("primaryAction", SectionFieldKind.List, "The one thing you want the visitor to do. At most one — two competing buttons is how a hero stops working.",
                    ItemFields:
                    [
                        new("label", SectionFieldKind.Text, "What the button says. A verb.", Required: true, MaxLength: 40),
                        new("href", SectionFieldKind.Link, "Where it goes.", Required: true)
                    ],
                    MaxItems: 1)
            ]),

        new(SectionType.RichText, "Text",
            "Prose: the story, the explanation, the terms. Reach for this when the content is sentences rather than a pattern.",
            [
                new("title", SectionFieldKind.Text, "Optional heading above the text.", MaxLength: 120),
                new("body", SectionFieldKind.RichText, "The text itself. Headings, paragraphs, lists, links and emphasis.", Required: true),
                new("width", SectionFieldKind.Choice, "How wide the column is. Prose is easier to read narrow.", Choices: ["narrow", "wide"])
            ]),

        new(SectionType.FeatureGrid, "Features",
            "Three to six things this offers, as a scannable grid. For 'what we do' and 'what is included'.",
            [
                new("title", SectionFieldKind.Text, "Heading above the grid.", MaxLength: 120),
                new("items", SectionFieldKind.List, "One entry per thing offered. Three or six read best; two looks unfinished and seven stops being scannable.",
                    ItemFields:
                    [
                        new("title", SectionFieldKind.Text, "The thing itself, named as the customer would name it.", Required: true, MaxLength: 80),
                        new("description", SectionFieldKind.Text, "One sentence on what it means for them.", MaxLength: 200),
                        new("icon", SectionFieldKind.Choice, "A glyph from the built-in set.",
                            Choices: ["check", "star", "clock", "shield", "spark", "heart", "chat", "tool", "leaf", "truck"])
                    ],
                    Required: true, MaxItems: 8)
            ]),

        new(SectionType.Testimonials, "Testimonials",
            "What customers said, quoted. The most persuasive section on most small-business sites, and the one nobody should invent content for.",
            [
                new("title", SectionFieldKind.Text, "Heading above the quotes.", MaxLength: 120),
                new("items", SectionFieldKind.List, "Real quotes only. If the person has none yet, leave the section out rather than writing plausible ones.",
                    ItemFields:
                    [
                        new("quote", SectionFieldKind.Text, "What they said, in their words.", Required: true, MaxLength: 400),
                        new("author", SectionFieldKind.Text, "Who said it.", Required: true, MaxLength: 80),
                        new("role", SectionFieldKind.Text, "Their company or role, if it adds weight.", MaxLength: 120),
                        new("photo", SectionFieldKind.Image, "Their photo.")
                    ],
                    Required: true, MaxItems: 12)
            ]),

        new(SectionType.Faq, "FAQ",
            "The questions people actually ask before buying. Also emitted as FAQ structured data, so it can win a search result on its own.",
            [
                new("title", SectionFieldKind.Text, "Heading above the list.", MaxLength: 120),
                new("items", SectionFieldKind.List, "Question and answer pairs, most-asked first.",
                    ItemFields:
                    [
                        new("question", SectionFieldKind.Text, "Phrased the way a customer would ask it, not the way the business would.", Required: true, MaxLength: 200),
                        new("answer", SectionFieldKind.RichText, "The answer, straight away. No preamble.", Required: true)
                    ],
                    Required: true, MaxItems: 20)
            ]),

        new(SectionType.Cta, "Call to action",
            "One sentence and one button, for the bottom of a page. The section that turns a page someone finished reading into a phone call.",
            [
                new("headline", SectionFieldKind.Text, "The ask, as a sentence.", Required: true, MaxLength: 120),
                new("body", SectionFieldKind.Text, "One line that removes the last objection.", MaxLength: 240),
                new("actionLabel", SectionFieldKind.Text, "What the button says.", Required: true, MaxLength: 40),
                new("actionHref", SectionFieldKind.Link, "Where the button goes.", Required: true),
                new("tone", SectionFieldKind.Choice, "How loud the band is.", Choices: ["solid", "soft", "plain"])
            ])
    ];

    private static readonly Dictionary<SectionType, SectionSchema> ByType = All.ToDictionary(x => x.Type);

    /// <summary>
    /// The schema for a section type. Throws rather than returning null: every value of the enum is in
    /// the catalogue, and a missing one is a programming error rather than a request that failed —
    /// <c>SectionCatalogueTests.Every_section_type_has_a_schema_and_a_renderer</c> is what keeps that
    /// true.
    /// </summary>
    public static SectionSchema For(SectionType type) =>
        ByType.TryGetValue(type, out var schema)
            ? schema
            : throw new InvalidOperationException($"No schema is declared for section type '{type}'.");

    /// <summary>
    /// A new section's props: every required field present and empty, every choice field set to its
    /// first option. So that adding a section always produces a valid document — a section that fails
    /// its own validator the moment it is created would make "add, then fill in" impossible, which is
    /// how a person works and how the agent works too.
    /// </summary>
    public static JsonObject DefaultProps(SectionType type)
    {
        var props = new JsonObject();

        foreach (var field in For(type).Fields)
        {
            if (field.Kind == SectionFieldKind.Choice && field.Choices is { Count: > 0 })
                props[field.Name] = field.Choices[0];
            else if (field.Kind == SectionFieldKind.List && field.Required)
                props[field.Name] = new JsonArray();
            else if (field.Required)
                props[field.Name] = field.Kind switch
                {
                    SectionFieldKind.Number => 0,
                    SectionFieldKind.Boolean => false,
                    _ => string.Empty
                };
        }

        return props;
    }

    /// <summary>
    /// The catalogue as text for the agent's instructions: every type, what it is for, and its fields.
    /// Generated rather than written out in the prompt, because a prompt that lists fields by hand is a
    /// second declaration of the same thing and it is the one that will go stale.
    /// </summary>
    public static string Describe()
    {
        var text = new StringBuilder();

        foreach (var schema in All)
        {
            text.AppendLine($"## {schema.Type} — {schema.Label}");
            text.AppendLine(schema.Purpose);
            text.AppendLine();

            foreach (var field in schema.Fields)
                Describe(text, field, indent: string.Empty);

            text.AppendLine();
        }

        return text.ToString();
    }

    private static void Describe(StringBuilder text, SectionFieldSchema field, string indent)
    {
        var bounds = new List<string>();
        if (field.Required) bounds.Add("required");
        if (field.MaxLength is { } max) bounds.Add($"max {max} chars");
        if (field.MaxItems is { } items) bounds.Add($"max {items} items");
        if (field.Choices is { Count: > 0 }) bounds.Add($"one of: {string.Join(", ", field.Choices)}");

        var suffix = bounds.Count == 0 ? string.Empty : $" ({string.Join("; ", bounds)})";
        text.AppendLine($"{indent}- `{field.Name}`: {field.Kind}{suffix} — {field.Description}");

        foreach (var item in field.ItemFields ?? [])
            Describe(text, item, indent + "  ");
    }
}
