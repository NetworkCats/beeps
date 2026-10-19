using System.Text.Json.Serialization;
using Beeps.Features.Localization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Islands;

// The islands of the layout (Pages/Shared/_Layout.cshtml): what every signed-in page has around its content.

/// <summary>The signed-in person's menu: in the navigation rail (wide screens) or the top bar (narrow ones).</summary>
public sealed record AccountMenuProps([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Rail);

public sealed record OptionProps(string Value, string Label, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Lang = null);

/// <summary>Where <c>_SiteOptions.cshtml</c> is drawn: wherever the screen has no side column, a "more" menu holds its footer.</summary>
public enum SiteOptionsPlace
{
    /// <summary>The side column's footer (wide screens).</summary>
    Footer,

    /// <summary>The top bar's "more" menu (narrow screens).</summary>
    TopBar,

    /// <summary>The navigation rail's "more" menu (screens in between, and wide pages with no side column).</summary>
    Rail,
}

/// <summary>The language and colour scheme switches of <c>_SiteOptions.cshtml</c>.</summary>
/// <param name="Menu">In a "more" menu, rather than the footer.</param>
/// <param name="Rail">The menu is the navigation rail's, rather than the top bar's.</param>
/// <param name="SignedIn">The choice is posted and saved to the account; signed out it is a GET, with no antiforgery token.</param>
/// <param name="Theme">For the markup without script; the app follows the theme as it is switched in place.</param>
public sealed record SiteOptionsProps(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Menu,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Rail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool SignedIn,
    string ReturnUrl,
    string Culture,
    IReadOnlyList<OptionProps> Cultures,
    [property: JsonIgnore] string Theme,
    IReadOnlyList<OptionProps> Themes,
    int Year)
{
    /// <summary>The languages on offer, each marked as written in itself; settings offers the same.</summary>
    public static IReadOnlyList<OptionProps> CulturesFor(IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return [.. SupportedCultures.All.Select(c => new OptionProps(c, localizer[$"Language.{c}"], c))];
    }

    public static IReadOnlyList<OptionProps> ThemesFor(IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return [.. Beeps.Features.Site.Themes.All.Select(t => new OptionProps(t, localizer[$"Theme.{t}"]))];
    }
}

/// <summary>What the last action did, shown as toasts.</summary>
public sealed record ToastsProps(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Alert);

/// <summary>Turnstile's widget on the forms that need it (<c>_Captcha.cshtml</c>).</summary>
/// <param name="Language">Turnstile's name for the page language.</param>
public sealed record CaptchaProps(string SiteKey, string Language);

/// <summary>The passkey button on the login page and on a sign-in's second step (<c>_PasskeySignIn.cshtml</c>, PLAN 6.14).</summary>
/// <param name="Options">Where the browser's options are asked for (posted, answered with JSON).</param>
/// <param name="Action">Where what the authenticator signed is posted, as a form, to sign in.</param>
/// <param name="Antiforgery">The token both carry: the page is not cached, so it may set its cookie.</param>
/// <param name="ReturnUrl">Where signing in leads once done, as the login page was asked for it.</param>
/// <param name="SecondStep">The second step after the password: one of the account's own passkeys, not any.</param>
public sealed record PasskeySignInProps(
    string Options,
    string Action,
    ClientAntiforgery Antiforgery,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReturnUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool SecondStep)
{
    /// <param name="page">The page whose handlers answer: <c>PasskeyOptions</c> and <c>Passkey</c>.</param>
    public static PasskeySignInProps For(HttpContext context, IAntiforgery antiforgery, IUrlHelper url, string page, string? returnUrl, bool secondStep)
    {
        ArgumentNullException.ThrowIfNull(antiforgery);
        ArgumentNullException.ThrowIfNull(url);
        var tokens = antiforgery.GetAndStoreTokens(context);
        return new PasskeySignInProps(
            url.Page(page, "PasskeyOptions")!,
            url.Page(page, "Passkey")!,
            new ClientAntiforgery(tokens.FormFieldName, tokens.RequestToken ?? ""),
            returnUrl,
            secondStep);
    }
}

/// <summary>
/// What an islands request (<see cref="IslandRequests"/>) says of the page besides its islands' props: what the app
/// needs to show it in place of the page shown (or of an app shell's skeleton), as a reload would.
/// </summary>
/// <param name="Lang">The page's language: in another one the layout's text is different too, and the page is loaded in full.</param>
/// <param name="Auth">How the request is signed in, as the layout's <c>data-auth</c> says it; null signed out.</param>
/// <param name="LiveFrom">When the page was drawn (<c>data-live-from</c>): a page fetched ahead is followed live from then.</param>
/// <param name="Main">The content column: the page's own markup around its islands, which come empty.</param>
public sealed record IslandPageProps(
    string Lang,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Auth,
    long LiveFrom,
    ClientConfigProps Config,
    PageShellProps Shell,
    string Main);
