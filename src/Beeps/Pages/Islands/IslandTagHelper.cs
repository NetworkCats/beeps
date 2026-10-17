using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Beeps.Pages.Islands;

/// <summary>
/// <c>&lt;island name="feed" props="…"&gt;markup&lt;/island&gt;</c>: a part of the page drawn by the Preact component of
/// the same name (<c>Client/app/islands.tsx</c>), from its props (PLAN 6.9); for signed-in people it follows the site live.
/// </summary>
/// <remarks>
/// <c>&lt;div class="island" data-island="feed"&gt;</c> (laid out as if it were not there) holds the props. Signed out it
/// also holds the markup inside, drawn here: what search engines read, and what the page shows before the script runs,
/// which then draws the very same markup over it; the props hold nothing personal, so the page can still be cached for
/// everyone. Signed in, the component draws the island from its props alone, so the markup inside is never drawn (and
/// parts only signed-in people see have no markup here at all); a page island then shows its <see cref="Skeleton"/>
/// until the component has drawn it. For an islands request (<see cref="IslandRequests"/>) only the props are kept. Before it come the chunks its component needs (<see cref="ClientChunks"/>), so they load
/// alongside the app.
/// </remarks>
[HtmlTargetElement("island", Attributes = "name")]
public sealed class IslandTagHelper(ClientChunks chunks, IHtmlHelper html) : TagHelper
{
    public string Name { get; set; } = "";

    /// <summary>A record registered in <see cref="IslandJsonContext"/>; null when the component needs none.</summary>
    public object? Props { get; set; }

    /// <summary>Drawn by the component alone, signed out too: a page nobody needs to find in a search engine (search results).</summary>
    public bool ClientOnly { get; set; }

    /// <summary>The wrapper element: <c>span</c> inside phrasing content (a link, a label).</summary>
    public string Tag { get; set; } = "div";

    /// <summary>
    /// The shape shown while the component is on its way, when the island comes without markup (<c>_Skeleton.cshtml</c>):
    /// <c>feed</c>, <c>profile</c>, <c>list</c>, <c>form</c> or <c>table</c> for a page's island, <c>block</c> for one
    /// within a page; none for the small islands of the layout.
    /// </summary>
    public string? Skeleton { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
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

        output.PreElement.SetHtmlContent(chunks.ForIsland(http, Name));
        output.TagName = Tag;
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "island");
        output.Attributes.SetAttribute("data-island", Name);
        if (ClientOnly || http.User.IsHuman())
        {
            if (Skeleton is null)
            {
                output.Content.SetHtmlContent(string.Empty);
            }
            else
            {
                ((IViewContextAware)html).Contextualize(ViewContext);
                output.Content.SetHtmlContent(await html.PartialAsync("_Skeleton", Skeleton));
            }
        }

        var script = new TagBuilder("script");
        script.Attributes["type"] = "application/json";
        script.Attributes["data-island-props"] = null;
        script.InnerHtml.AppendHtml(envelope);
        output.PreContent.SetHtmlContent(script);
    }
}
