using System.Text;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Rendering.Sections;

public class RichTextSectionRenderer : ISectionRenderer
{
    public SectionType Type => SectionType.RichText;

    public string Render(SiteSection section, SectionRenderContext context)
    {
        var width = section.Choice("width", "narrow");
        var markup = new StringBuilder();

        markup.Append($"<section class=\"prose prose--{width}\"><div class=\"wrap\">");

        if (section.TextOrNull("title") is { } title)
            markup.Append($"<h2>{title}</h2>");

        markup.Append($"<div class=\"prose__body\">{section.RichTextHtml("body")}</div>");
        markup.Append("</div></section>");

        return markup.ToString();
    }
}
