using System.Text.Json.Serialization;

namespace Beeps.Pages.Islands;

// The islands of the layout (Pages/Shared/_Layout.cshtml): what every signed-in page has around its content.

/// <summary>The signed-in person's menu: in the navigation rail (wide screens) or the top bar (narrow ones).</summary>
public sealed record AccountMenuProps([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Rail);

public sealed record OptionProps(string Value, string Label, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Lang = null);

/// <summary>The language and colour scheme switches of <c>_SiteOptions.cshtml</c>.</summary>
/// <param name="Menu">In the top bar's "more" menu, rather than the footer.</param>
/// <param name="SignedIn">The choice is posted and saved to the account; signed out it is a GET, with no antiforgery token.</param>
public sealed record SiteOptionsProps(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Menu,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool SignedIn,
    string ReturnUrl,
    string Culture,
    IReadOnlyList<OptionProps> Cultures,
    string Theme,
    IReadOnlyList<OptionProps> Themes,
    int Year);

/// <summary>What the last action did, shown as toasts.</summary>
public sealed record ToastsProps(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Alert);

/// <summary>Turnstile's widget on the forms that need it (<c>_Captcha.cshtml</c>).</summary>
/// <param name="Language">Turnstile's name for the page language.</param>
public sealed record CaptchaProps(string SiteKey, string Language);
