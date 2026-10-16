using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Beeps.Pages.TagHelpers;

/// <summary>
/// <c>&lt;field-error name="Email" /&gt;</c>: the field's messages, rendered only when there are any, because
/// collection-ui marks a whole field red as soon as it contains an <c>.error</c> element.
/// </summary>
[HtmlTargetElement("field-error", Attributes = "name", TagStructure = TagStructure.WithoutEndTag)]
public sealed class FieldErrorTagHelper : TagHelper
{
    public string Name { get; set; } = "";

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var errors = ViewContext.ModelState.TryGetValue(Name, out var entry) ? entry.Errors : null;
        if (errors is not { Count: > 0 })
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "p";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "error");
        output.Attributes.SetAttribute("id", ErrorId(Name));
        output.Content.SetContent(string.Join(" ", errors.Select(e => e.ErrorMessage)));
    }

    public static string ErrorId(string name) => $"{name}-error";
}

/// <summary>
/// Names an input (or textarea) after its field and adds <c>aria-invalid</c> and <c>aria-describedby</c>
/// when the field has errors: <c>&lt;input beeps-field="Email"&gt;</c>.
/// </summary>
[HtmlTargetElement("input", Attributes = "beeps-field", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("textarea", Attributes = "beeps-field")]
public sealed class FieldInputTagHelper : TagHelper
{
    [HtmlAttributeName("beeps-field")]
    public string Field { get; set; } = "";

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Attributes.SetAttribute("name", Field);
        output.Attributes.SetAttribute("id", Field);
        if (ViewContext.ModelState.TryGetValue(Field, out var entry) && entry.Errors.Count > 0)
        {
            output.Attributes.SetAttribute("aria-invalid", "true");
            output.Attributes.SetAttribute("aria-describedby", FieldErrorTagHelper.ErrorId(Field));
        }
    }
}

/// <summary><c>&lt;form-errors /&gt;</c>: errors that belong to no single field, as an alert.</summary>
[HtmlTargetElement("form-errors", TagStructure = TagStructure.WithoutEndTag)]
public sealed class FormErrorsTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var errors = ViewContext.ModelState.TryGetValue(string.Empty, out var entry) ? entry.Errors : null;
        if (errors is not { Count: > 0 })
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "alert alert-danger");
        output.Attributes.SetAttribute("role", "alert");
        var content = new TagBuilder("p");
        content.AddCssClass("alert-text");
        content.InnerHtml.Append(string.Join(" ", errors.Select(e => e.ErrorMessage)));
        output.Content.SetHtmlContent(content);
    }
}
