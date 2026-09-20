using System.Text;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Rendering.Sections;

public class HeroSectionRenderer : ISectionRenderer
{
    public SectionType Type => SectionType.Hero;

    public string Render(SiteSection section, SectionRenderContext context)
    {
        var layout = section.Choice("layout", "imageRight");
        var image = context.ResolveImage(section.Raw("image"));
        var action = section.Items("primaryAction").FirstOrDefault();

        var markup = new StringBuilder();

        markup.Append($"<section class=\"hero hero--{layout}\">");
        markup.Append("<div class=\"wrap hero__inner\">");
        markup.Append("<div class=\"hero__text\">");
        markup.Append($"<h1>{section.Text("headline")}</h1>");

        if (section.TextOrNull("subheadline") is { } subheadline)
            markup.Append($"<p class=\"lede\">{subheadline}</p>");

        if (action is not null && action.TextOrNull("label") is { } label)
            markup.Append($"<a class=\"button button--primary\" href=\"{SectionMarkup.Attribute(context.ResolveHref(action.Raw("href")))}\">{label}</a>");

        markup.Append("</div>");

        // The image is decorative here: the headline beside it already carries the meaning, so an empty
        // alt is the correct answer rather than a missing attribute or a repeat of the headline.
        if (image is not null && layout != "textOnly")
            markup.Append($"<div class=\"hero__media\"><img src=\"{SectionMarkup.Attribute(image)}\" alt=\"\" loading=\"eager\"></div>");

        markup.Append("</div></section>");

        return markup.ToString();
    }
}
