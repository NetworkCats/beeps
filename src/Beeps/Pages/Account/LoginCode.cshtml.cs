using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Account;

/// <summary>
/// The second step of signing in to an account past its wrong passwords: the code sent to its address, if the password was
/// right (AccountService.LoginAsync).
/// </summary>
public sealed class LoginCodeModel(AccountService accounts, IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>
    /// The account the password was right for, as the first step left it in this browser's TempData (encrypted), never
    /// as posted: anyone could name any account there and use up the attempts of the code sent to its owner.
    /// </summary>
    public string? Login { get; private set; }

    [BindProperty] public string? Code { get; set; }

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        if (User.IsHuman())
        {
            return LocalRedirect("~/");
        }

        Login = TempData.Peek(LoginModel.LoginKey) as string;
        // Opened by itself (or again, the name long gone): there is nothing to sign in to here.
        return string.IsNullOrEmpty(Login) ? RedirectToPage("/Account/Login", new { ReturnUrl }) : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (User.IsHuman())
        {
            return this.AlreadySignedIn(localizer);
        }

        Login = TempData.Peek(LoginModel.LoginKey) as string;
        var result = await accounts.LoginWithCodeAsync(Login, Code, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ShowError(result.Error);
        }

        TempData.Remove(LoginModel.LoginKey);
        return await LoginModel.SignInAfterFirstStepAsync(this, accounts, result.Value, ReturnUrl, cancellationToken);
    }
}
