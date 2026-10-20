using Beeps.Features.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Account;

/// <summary>
/// The code sent by ForgotPassword, for the address it was given as it left it in this browser's TempData (encrypted),
/// never as posted, with the stamp of the code: anyone could name any address and use up the attempts of the code
/// sent to its owner.
/// </summary>
public sealed class ResetPasswordModel(AccountService accounts, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public string? Email { get; private set; }

    [BindProperty] public string? Code { get; set; }

    [BindProperty] public string? Password { get; set; }

    public IActionResult OnGet()
    {
        Email = TempData.Peek(ForgotPasswordModel.EmailKey) as string;
        // Opened by itself (or again, the address long gone): there is no code to enter here.
        return string.IsNullOrEmpty(Email) ? RedirectToPage("/Account/ForgotPassword") : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Email = TempData.Peek(ForgotPasswordModel.EmailKey) as string;
        if (string.IsNullOrEmpty(Email))
        {
            return RedirectToPage("/Account/ForgotPassword");
        }

        var stamp = TempData.Peek(ForgotPasswordModel.CodeStampKey) as string;
        var result = await accounts.ResetPasswordAsync(new PasswordResetRequest(Email, Code, Password, stamp), this.Origin(), cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ShowError(result.Error);
        }

        TempData.Remove(ForgotPasswordModel.EmailKey);
        TempData.Remove(ForgotPasswordModel.CodeStampKey);
        TempData[Notices.Success] = localizer["Reset.Done"].Value;
        return RedirectToPage("/Account/Login");
    }
}
