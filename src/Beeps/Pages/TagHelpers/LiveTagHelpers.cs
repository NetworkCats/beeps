using System.Globalization;
using Beeps.Features.Auth;
using Beeps.Features.Live;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Beeps.Pages.TagHelpers;

/// <summary>
/// <c>&lt;body live-stream&gt;</c>: for signed-in people only (PLAN 6.10), where the live stream is, when the page was
/// rendered (the stream replays what happened since) and how the request is signed in (<c>data-auth</c>, so the
/// browser's other tabs follow a sign-in or sign-out). Razor writes <c>data-*</c> attributes even when their value is
/// null, so they are not written by hand: signed-out pages stay free of them, and of the cache-busting render time.
/// What each page follows is in its islands' props (<c>Pages/Islands</c>).
/// </summary>
[HtmlTargetElement("body", Attributes = StreamAttribute)]
public sealed class LiveTagHelper(TimeProvider time) : TagHelper
{
    private const string StreamAttribute = "live-stream";

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);
        // A marker without a value, so not bound: it must not reach the page either way.
        output.Attributes.RemoveAll(StreamAttribute);
        var user = ViewContext.HttpContext.User;
        if (!user.IsHuman())
        {
            return;
        }

        output.Attributes.SetAttribute("data-auth", user.AuthState());
        // Unverified too: such a tab follows its account (verified or signed out elsewhere), nothing else.
        output.Attributes.SetAttribute("data-live-stream", LiveEndpoints.WebPath);
        output.Attributes.SetAttribute("data-live-from", time.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
    }
}
