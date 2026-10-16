using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Account;

public sealed class LoginModel(AccountService accounts, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty] public string? Login { get; set; }

    [BindProperty] public string? Password { get; set; }

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => User.IsHuman() ? LocalRedirect("~/") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // A form from a tab opened before signing in; switching accounts needs a sign-out first.
        if (User.IsHuman())
        {
            return this.AlreadySignedIn(localizer);
        }

        var result = await accounts.LoginAsync(Login, Password, this.Origin(), cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ShowError(result.Error);
        }

        await HttpContext.SignInHumanAsync(result.Value);
        if (!result.Value.EmailVerified)
        {
            return RedirectToPage("/Onboarding/Verify");
        }

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "~/");
    }
}
