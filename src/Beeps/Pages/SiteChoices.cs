using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Features.Localization;
using Beeps.Features.Shared;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages;

/// <summary>
/// The language and the colour scheme picked (the footer, the menus, settings): kept in a cookie, and saved to the
/// account when signed in. A choice not on offer changes neither.
/// </summary>
public static class SiteChoices
{
    public static async Task<Result> ChooseCultureAsync(this PageModel page, AccountService accounts, string? culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(accounts);
        var result = page.User.IsHuman() ? await accounts.SetLocaleAsync(page.User.ActorId(), culture, cancellationToken) : Result.Success;
        if (result.IsSuccess && SupportedCultures.Find(culture) is { } supported)
        {
            CultureCookie.Append(page.Response, supported);
        }

        return result;
    }

    public static async Task<Result> ChooseThemeAsync(this PageModel page, AccountService accounts, string? theme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(accounts);
        var result = page.User.IsHuman() ? await accounts.SetThemeAsync(page.User.ActorId(), theme, cancellationToken) : Result.Success;
        if (result.IsSuccess && Themes.Find(theme) is { } supported)
        {
            ThemeCookie.Set(page.Response, supported);
        }

        return result;
    }
}
