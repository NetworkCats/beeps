using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages;

/// <summary>Colour scheme switcher target: remembers the choice (cookie, and the account when signed in) and goes back.</summary>
public sealed class ThemeModel(AccountService accounts) : PageModel
{
    /// <summary>
    /// Signed-out visitors switch with a GET form, so cached public pages need no antiforgery token. It only sets
    /// their own cookie; a signed-in account's scheme is changed by POST alone.
    /// </summary>
    public IActionResult OnGet(string? theme, string? returnUrl)
    {
        if (!User.IsHuman() && Themes.Find(theme) is { } supported)
        {
            ThemeCookie.Set(Response, supported);
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
    }

    /// <summary>The app (shell/SiteOptions.tsx) sends the footer's and the menu's choice here in the background, and switches the page itself.</summary>
    public async Task<IActionResult> OnPostAsync(string? theme, string? returnUrl, CancellationToken cancellationToken)
    {
        await this.ChooseThemeAsync(accounts, theme, cancellationToken);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
    }
}
