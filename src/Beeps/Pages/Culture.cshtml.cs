using Beeps.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages;

/// <summary>Language switcher target: remembers the choice in a cookie and goes back.</summary>
public sealed class CultureModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect("~/");

    public IActionResult OnPost(string? culture, string? returnUrl)
    {
        if (SupportedCultures.Find(culture) is { } supported)
        {
            CultureCookie.Append(Response, supported);
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
    }
}
