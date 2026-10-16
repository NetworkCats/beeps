using Beeps.Features.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Account;

public sealed class ResetPasswordModel(AccountService accounts, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty] public string? Email { get; set; }

    [BindProperty] public string? Code { get; set; }

    [BindProperty] public string? Password { get; set; }

    public void OnGet() => Email = TempData[ForgotPasswordModel.EmailKey] as string;

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await accounts.ResetPasswordAsync(new PasswordResetRequest(Email, Code, Password), cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ShowError(result.Error);
        }

        TempData[Notices.Success] = localizer["Reset.Done"].Value;
        return RedirectToPage("/Account/Login");
    }
}
