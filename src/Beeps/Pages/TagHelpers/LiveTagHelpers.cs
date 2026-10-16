using System.Globalization;
using Beeps.Features.Auth;
using Beeps.Features.Live;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Beeps.Pages.TagHelpers;

/// <summary>
/// The <c>data-live-*</c> attributes the live script reads (<c>Client/ts/live.ts</c>), written for signed-in members
/// only (PLAN 6.10). Razor writes <c>data-*</c> attributes even when their value is null, so they are not written by
/// hand: signed-out pages stay free of them, and of the cache-busting render time.
/// </summary>
/// <remarks>
/// <c>&lt;body live-stream&gt;</c>: where the stream is and when the page was rendered.
/// <c>live-watch="Model.Live"</c>: the page's <see cref="LiveTopic"/>.
/// <c>live-agents="true"</c>: reload when one of the viewer's agents connects.
/// </remarks>
[HtmlTargetElement("body", Attributes = StreamAttribute)]
[HtmlTargetElement(Attributes = WatchAttribute)]
[HtmlTargetElement(Attributes = AgentsAttribute)]
public sealed class LiveTagHelper(TimeProvider time) : TagHelper
{
    private const string StreamAttribute = "live-stream";
    private const string WatchAttribute = "live-watch";
    private const string AgentsAttribute = "live-agents";

    [HtmlAttributeName(WatchAttribute)]
    public LiveTopic? Watch { get; set; }

    [HtmlAttributeName(AgentsAttribute)]
    public bool Agents { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);
        // A marker without a value, so not bound: it must not reach the page either way.
        output.Attributes.RemoveAll(StreamAttribute);
        if (!ViewContext.HttpContext.User.IsMember())
        {
            return;
        }

        if (context.AllAttributes.ContainsName(StreamAttribute))
        {
            output.Attributes.SetAttribute("data-live-stream", LiveEndpoints.WebPath);
            // The stream replays what happened between rendering this page and connecting.
            output.Attributes.SetAttribute("data-live-from", time.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        }

        if (Watch is not null)
        {
            output.Attributes.SetAttribute("data-live-watch", Watch.ToString());
        }

        if (Agents)
        {
            output.Attributes.SetAttribute("data-live-agents", "");
        }
    }
}
