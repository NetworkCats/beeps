using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Beeps.Pages.Islands;

/// <summary>
/// <c>&lt;island name="feed" props="…"&gt;markup&lt;/island&gt;</c>: a part of the page that a Preact component of the
/// same name (<c>Client/app/islands.tsx</c>) takes over (PLAN 6.9); for signed-in people it follows the site live.
/// </summary>
/// <remarks>
/// The markup is wrapped in <c>&lt;div class="island" data-island="feed"&gt;</c> (laid out as if it were not there) with
/// the props in front of it, so the page looks right before the script runs, and the component then draws the very
/// same markup over it. Signed out, the props hold nothing personal, so the page can still be cached for everyone. For
/// an islands request (<see cref="IslandRequests"/>) only the props are kept, and the markup is not drawn at all.
/// </remarks>
[HtmlTargetElement("island", Attributes = "name")]
public sealed class IslandTagHelper : TagHelper
{
    public string Name { get; set; } = "";

    /// <summary>A record registered in <see cref="IslandJsonContext"/>; null when the component needs none.</summary>
    public object? Props { get; set; }

    /// <summary>The wrapper element: <c>span</c> inside phrasing content (a link, a label).</summary>
    public string Tag { get; set; } = "div";

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var http = ViewContext.HttpContext;
        var envelope = IslandJson.Envelope(Props, ViewContext.ModelState);
        if (IslandRequests.IsIslandRequest(http))
        {
            IslandRequests.Collected(http)[Name] = envelope;
            output.SuppressOutput();
            return;
        }

        output.TagName = Tag;
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "island");
        output.Attributes.SetAttribute("data-island", Name);
        var script = new TagBuilder("script");
        script.Attributes["type"] = "application/json";
        script.Attributes["data-island-props"] = null;
        script.InnerHtml.AppendHtml(envelope);
        output.PreContent.SetHtmlContent(script);
    }
}
