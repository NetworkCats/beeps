using Beeps.Features.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Account;

public sealed class ForgotPasswordModel(AccountService accounts) : PageModel
{
    public const string EmailKey = "ResetEmail";
    public const string CodeStampKey = "ResetCodeStamp";

    [BindProperty] public string? Email { get; set; }

    [BindProperty(Name = "cf-turnstile-response")] public string? CaptchaToken { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await accounts.RequestPasswordResetAsync(Email, CaptchaToken, this.Origin(), cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ShowError(result.Error);
        }

        // Carried in TempData, not the URL, so the address stays out of logs and history, and the stamp with only this
        // browser (AccountService.ResetPasswordAsync).
        TempData[EmailKey] = Email?.Trim();
        TempData[CodeStampKey] = result.Value;
        return RedirectToPage("/Account/ResetPassword");
    }
}
