using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Account;

public sealed class LoginModel(AccountService accounts, IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>The name the code page signs in, carried in TempData, not the URL, as the reset page's address is.</summary>
    public const string LoginKey = "LoginName";

    [BindProperty] public string? Login { get; set; }

    [BindProperty] public string? Password { get; set; }

    [BindProperty(Name = "cf-turnstile-response")] public string? CaptchaToken { get; set; }

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    /// <summary>The account is past its wrong passwords: the form takes Turnstile from now on.</summary>
    public bool NeedsCaptcha { get; private set; }

    public IActionResult OnGet() => User.IsHuman() ? LocalRedirect("~/") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // A form from a tab opened before signing in; switching accounts needs a sign-out first.
        if (User.IsHuman())
        {
            return this.AlreadySignedIn(localizer);
        }

        var result = await accounts.LoginAsync(new LoginRequest(Login, Password, CaptchaToken), this.Origin(), cancellationToken);
        if (!result.IsSuccess)
        {
            NeedsCaptcha = result.Error.Code is AccountErrorCodes.CaptchaRequired or AccountErrorCodes.CaptchaFailed;
            return this.ShowError(result.Error);
        }

        if (result.Value.Account is not { } account)
        {
            TempData[LoginKey] = Login?.Trim();
            return RedirectToPage("/Account/LoginCode", new { ReturnUrl });
        }

        return await SignInAsync(this, account, ReturnUrl);
    }

    /// <summary>Signs in and goes on: to verifying the address first, if it is not yet.</summary>
    internal static async Task<IActionResult> SignInAsync(PageModel page, AccountState account, string? returnUrl)
    {
        await page.HttpContext.SignInHumanAsync(account);
        if (!account.EmailVerified)
        {
            return page.RedirectToPage("/Onboarding/Verify");
        }

        return page.LocalRedirect(page.Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
    }
}
