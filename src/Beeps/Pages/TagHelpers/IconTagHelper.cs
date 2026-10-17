using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Beeps.Pages.TagHelpers;

/// <summary>
/// <c>&lt;icon name="home" /&gt;</c> → <c>&lt;span class="icon" aria-hidden="true"&gt;home&lt;/span&gt;</c>: a Material Symbols
/// glyph, drawn from the icon font's ligature. Decorative only; the text next to it (or a visually hidden label) says what
/// it means. The font holds only the icons in <c>Client/icons.json</c>, so a new name must be added there (npm test checks).
/// A <c>class</c> on the tag is added to <c>icon</c>.
/// </summary>
[HtmlTargetElement("icon", Attributes = "name", TagStructure = TagStructure.WithoutEndTag)]
public sealed class IconTagHelper : TagHelper
{
    public string Name { get; set; } = "";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var extra = output.Attributes.TryGetAttribute("class", out var css) ? $" {css.Value}" : "";
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "icon" + extra);
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Content.SetContent(Name);
    }
}
