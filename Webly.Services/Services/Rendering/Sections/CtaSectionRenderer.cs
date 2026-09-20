using System.Text;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Rendering.Sections;

public class CtaSectionRenderer : ISectionRenderer
{
    public SectionType Type => SectionType.Cta;

    public string Render(SiteSection section, SectionRenderContext context)
    {
        var tone = section.Choice("tone", "solid");
        var markup = new StringBuilder();

        markup.Append($"<section class=\"cta cta--{tone}\"><div class=\"wrap cta__inner\">");
        markup.Append($"<h2>{section.Text("headline")}</h2>");

        if (section.TextOrNull("body") is { } body)
            markup.Append($"<p>{body}</p>");

        markup.Append($"<a class=\"button button--primary\" href=\"{SectionMarkup.Attribute(context.ResolveHref(section.Raw("actionHref")))}\">{section.Text("actionLabel")}</a>");
        markup.Append("</div></section>");

        return markup.ToString();
    }
}
