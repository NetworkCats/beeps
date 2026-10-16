using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages;

/// <summary>Language switcher target: remembers the choice (cookie, and the account when signed in) and goes back.</summary>
public sealed class CultureModel(AccountService accounts) : PageModel
{
    public IActionResult OnGet() => LocalRedirect("~/");

    public async Task<IActionResult> OnPostAsync(string? culture, string? returnUrl, CancellationToken cancellationToken)
    {
        if (SupportedCultures.Find(culture) is { } supported)
        {
            CultureCookie.Append(Response, supported);
            if (User.IsHuman())
            {
                await accounts.SetLocaleAsync(User.ActorId(), supported, cancellationToken);
            }
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
    }
}
