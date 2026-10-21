using System.Globalization;
using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Beeps.Pages.TagHelpers;

/// <summary>
/// <c>&lt;utc-time value="@x" /&gt;</c> → <c>&lt;time datetime="…Z"&gt;…Z&lt;/time&gt;</c>. The server never formats times
/// for display; the client turns this into a relative time in the viewer's time zone.
/// </summary>
[HtmlTargetElement("utc-time", Attributes = "value", TagStructure = TagStructure.WithoutEndTag)]
public sealed class UtcTimeTagHelper : TagHelper
{
    public DateTimeOffset? Value { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (Value is not { } value)
        {
            output.SuppressOutput();
            return;
        }

        var text = value.UtcDateTime.ToString(UtcTimestampJsonConverter.Format, CultureInfo.InvariantCulture);
        output.TagName = "time";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("datetime", text);
        output.Content.SetContent(text);
    }
}
