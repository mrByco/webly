using System.Text;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Rendering.Sections;

public class FeatureGridSectionRenderer : ISectionRenderer
{
    public SectionType Type => SectionType.FeatureGrid;

    public string Render(SiteSection section, SectionRenderContext context)
    {
        var items = section.Items("items").ToList();
        var markup = new StringBuilder();

        markup.Append("<section class=\"features\"><div class=\"wrap\">");

        if (section.TextOrNull("title") is { } title)
            markup.Append($"<h2>{title}</h2>");

        // The column count comes from the item count rather than a prop: three items in a four-column
        // grid leaves a hole, and nobody should have to think about that.
        markup.Append($"<ul class=\"features__grid features__grid--{Math.Min(items.Count, 4)}\">");

        foreach (var item in items)
        {
            markup.Append("<li class=\"feature\">");
            markup.Append(Icons.Svg(item.Raw("icon")));
            markup.Append($"<h3>{item.Text("title")}</h3>");

            if (item.TextOrNull("description") is { } description)
                markup.Append($"<p>{description}</p>");

            markup.Append("</li>");
        }

        markup.Append("</ul></div></section>");

        return markup.ToString();
    }
}
