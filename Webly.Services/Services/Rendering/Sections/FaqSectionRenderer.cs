using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Rendering.Sections;

/// <summary>
/// The one section that emits structured data as well as markup. An FAQ can win its own search result,
/// and a small business's questions are often what people actually search for — so the schema.org block
/// is part of the section rather than an SEO setting somebody has to know to turn on.
/// </summary>
public class FaqSectionRenderer : ISectionRenderer
{
    public SectionType Type => SectionType.Faq;

    public string Render(SiteSection section, SectionRenderContext context)
    {
        var items = section.Items("items").ToList();
        var markup = new StringBuilder();

        markup.Append("<section class=\"faq\"><div class=\"wrap\">");

        if (section.TextOrNull("title") is { } title)
            markup.Append($"<h2>{title}</h2>");

        markup.Append("<div class=\"faq__list\">");

        foreach (var item in items)
        {
            markup.Append("<details class=\"faq__item\">");
            markup.Append($"<summary>{item.Text("question")}</summary>");
            markup.Append($"<div class=\"faq__answer\">{item.RichTextHtml("answer")}</div>");
            markup.Append("</details>");
        }

        markup.Append("</div>");
        markup.Append(StructuredData(items));
        markup.Append("</div></section>");

        return markup.ToString();
    }

    /// <summary>
    /// Built as JSON and serialized, never string-concatenated: a quote mark in a customer's question
    /// would break a hand-built JSON-LD block, and a broken one is worse than none — it invalidates the
    /// whole page's structured data rather than just this section's.
    /// </summary>
    private static string StructuredData(IReadOnlyList<JsonObject> items)
    {
        if (items.Count == 0) return string.Empty;

        var questions = new JsonArray();

        foreach (var item in items)
        {
            questions.Add(new JsonObject
            {
                ["@type"] = "Question",
                ["name"] = item.Raw("question"),
                ["acceptedAnswer"] = new JsonObject
                {
                    ["@type"] = "Answer",
                    ["text"] = item.Raw("answer")
                }
            });
        }

        var payload = new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "FAQPage",
            ["mainEntity"] = questions
        };

        // The one sequence that can end a script element early, whatever the JSON escaping did.
        var json = payload.ToJsonString(new JsonSerializerOptions()).Replace("</", "<\\/");

        return $"<script type=\"application/ld+json\">{json}</script>";
    }
}
