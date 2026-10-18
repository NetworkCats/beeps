using Beeps.Features.Auth;
using Microsoft.AspNetCore.Html;
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
/// everyone. Signed in, the component draws the island from its props alone, so the markup inside is never drawn: the
/// app takes a page's content from its data (<see cref="IslandRequests"/>), where the island comes empty and its props
/// beside the page, and only the layout's islands (and the answer to a form posted without the app) carry their props
/// here. Before it come the chunks its component needs (<see cref="ClientChunks"/>), so they load alongside the app.
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
    /// The shape a <see cref="ClientOnly"/> island shows signed out while its component is on its way (<c>_Skeleton.cshtml</c>).
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
            Wrap(output);
            output.Content.SetHtmlContent(string.Empty);
            return;
        }

        output.PreElement.SetHtmlContent(chunks.ForIsland(http, Name));
        Wrap(output);
        if (http.User.IsHuman())
        {
            output.Content.SetHtmlContent(string.Empty);
        }
        else if (ClientOnly)
        {
            ((IViewContextAware)html).Contextualize(ViewContext);
            output.Content.SetHtmlContent(Skeleton is null ? HtmlString.Empty : await html.PartialAsync("_Skeleton", Skeleton));
        }

        var script = new TagBuilder("script");
        script.Attributes["type"] = "application/json";
        script.Attributes["data-island-props"] = null;
        script.InnerHtml.AppendHtml(envelope);
        output.PreContent.SetHtmlContent(script);
    }

    private void Wrap(TagHelperOutput output)
    {
        output.TagName = Tag;
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "island");
        output.Attributes.SetAttribute("data-island", Name);
    }
}
