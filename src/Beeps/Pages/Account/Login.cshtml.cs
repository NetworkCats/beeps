using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Account;

public sealed class LoginModel(AccountService accounts) : PageModel
{
    [BindProperty] public string? Login { get; set; }

    [BindProperty] public string? Password { get; set; }

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
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
