using System.Globalization;
using Beeps.Features.Auth;
using Beeps.Features.Live;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Beeps.Pages.TagHelpers;

/// <summary>
/// The <c>data-live-*</c> attributes the live script reads (<c>Client/ts/live.ts</c>), written for signed-in people
/// only (PLAN 6.10; an unverified account gets the stream but nothing to watch). Razor writes <c>data-*</c> attributes
/// even when their value is null, so they are not written by hand: signed-out pages stay free of them, and of the
/// cache-busting render time.
/// </summary>
/// <remarks>
/// <c>&lt;body live-stream&gt;</c>: where the stream is and when the page was rendered, and for anyone signed in how
/// (<c>data-auth</c>), so the browser's other tabs follow a sign-in or sign-out.
/// <c>live-watch="Model.Live"</c>: the page's <see cref="LiveTopic"/>.
/// <c>live-region</c>, <c>live-subject</c>, <c>live-url</c>: the same <c>data-live-*</c> attributes, for members only.
/// <c>live-empty</c>: a live list's empty state, which the live script shows and hides as the list empties and fills.
/// </remarks>
[HtmlTargetElement("body", Attributes = StreamAttribute)]
[HtmlTargetElement(Attributes = WatchAttribute)]
[HtmlTargetElement(Attributes = RegionAttribute)]
[HtmlTargetElement(Attributes = SubjectAttribute)]
[HtmlTargetElement(Attributes = UrlAttribute)]
[HtmlTargetElement(Attributes = EmptyAttribute)]
public sealed class LiveTagHelper(TimeProvider time) : TagHelper
{
    private const string StreamAttribute = "live-stream";
    private const string WatchAttribute = "live-watch";
    private const string RegionAttribute = "live-region";
    private const string SubjectAttribute = "live-subject";
    private const string UrlAttribute = "live-url";
    private const string EmptyAttribute = "live-empty";

    [HtmlAttributeName(WatchAttribute)]
    public LiveTopic? Watch { get; set; }

    /// <summary><c>data-live-region</c> on pages that signed-out visitors see too, written for members only.</summary>
    [HtmlAttributeName(RegionAttribute)]
    public string? Region { get; set; }

    /// <summary><c>data-live-subject</c>: what the page is about (<c>beep:{id}</c>, <c>actor:{id}</c>).</summary>
    [HtmlAttributeName(SubjectAttribute)]
    public string? Subject { get; set; }

    /// <summary><c>data-live-url</c>: where the page's regions are drawn again from, when its own address can change.</summary>
    [HtmlAttributeName(UrlAttribute)]
    public string? Url { get; set; }


    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);
        // Markers without a value, so not bound: they must not reach the page either way.
        output.Attributes.RemoveAll(StreamAttribute);
        output.Attributes.RemoveAll(EmptyAttribute);
        var user = ViewContext.HttpContext.User;
        if (!user.IsHuman())
        {
            return;
        }

        if (context.AllAttributes.ContainsName(StreamAttribute))
        {
            // How this tab is signed in, for the other tabs of the browser to compare with (Client/ts/auth-sync.ts).
            output.Attributes.SetAttribute("data-auth", user.AuthState());
            // Unverified too: such a tab follows its account (verified or signed out elsewhere), nothing else.
            output.Attributes.SetAttribute("data-live-stream", LiveEndpoints.WebPath);
            // The stream replays what happened between rendering this page and connecting.
            output.Attributes.SetAttribute("data-live-from", time.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        }

        if (!user.IsMember())
        {
            return;
        }

        if (Watch is not null)
        {
            output.Attributes.SetAttribute("data-live-watch", Watch.ToString());
        }

        if (context.AllAttributes.ContainsName(EmptyAttribute))
        {
            output.Attributes.SetAttribute("data-live-empty", null);
        }

        foreach (var (name, value) in new[] { ("data-live-region", Region), ("data-live-subject", Subject), ("data-live-url", Url) })
        {
            if (value is not null)
            {
                output.Attributes.SetAttribute(name, value);
            }
        }
    }
}
