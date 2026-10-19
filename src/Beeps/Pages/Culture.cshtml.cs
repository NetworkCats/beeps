using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages;

/// <summary>Language switcher target: remembers the choice (cookie, and the account when signed in) and goes back.</summary>
public sealed class CultureModel(AccountService accounts) : PageModel
{
    /// <summary>
    /// Signed-out visitors switch with a GET form, so cached public pages need no antiforgery token. It only
    /// sets their own cookie; a signed-in account's language is changed by POST alone.
    /// </summary>
    public IActionResult OnGet(string? culture, string? returnUrl)
    {
        if (!User.IsHuman() && SupportedCultures.Find(culture) is { } supported)
        {
            CultureCookie.Append(Response, supported);
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
    }

    public async Task<IActionResult> OnPostAsync(string? culture, string? returnUrl, CancellationToken cancellationToken)
    {
        await this.ChooseCultureAsync(accounts, culture, cancellationToken);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
    }
}
