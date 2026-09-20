using System.Text;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Rendering.Sections;

public class TestimonialsSectionRenderer : ISectionRenderer
{
    public SectionType Type => SectionType.Testimonials;

    public string Render(SiteSection section, SectionRenderContext context)
    {
        var markup = new StringBuilder();

        markup.Append("<section class=\"quotes\"><div class=\"wrap\">");

        if (section.TextOrNull("title") is { } title)
            markup.Append($"<h2>{title}</h2>");

        markup.Append("<ul class=\"quotes__list\">");

        foreach (var item in section.Items("items"))
        {
            markup.Append("<li class=\"quote\">");
            markup.Append($"<blockquote>{item.Text("quote")}</blockquote>");
            markup.Append("<div class=\"quote__by\">");

            if (context.ResolveImage(item.Raw("photo")) is { } photo)
                markup.Append($"<img src=\"{SectionMarkup.Attribute(photo)}\" alt=\"\" loading=\"lazy\">");

            markup.Append($"<span class=\"quote__author\">{item.Text("author")}</span>");

            if (item.TextOrNull("role") is { } role)
                markup.Append($"<span class=\"quote__role\">{role}</span>");

            markup.Append("</div></li>");
        }

        markup.Append("</ul></div></section>");

        return markup.ToString();
    }
}
