using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Onboarding;

public sealed class VerifyModel(AccountService accounts, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty] public string? Code { get; set; }

    public IActionResult OnGet() =>
        User.FindFirst(BeepsClaims.EmailVerified)?.Value == "true" ? RedirectToPage("/Onboarding/Agent") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await accounts.VerifyEmailAsync(User.ActorId(), Code, cancellationToken);
        return result.IsSuccess ? RedirectToPage("/Onboarding/Agent") : this.ShowError(result.Error);
    }

    public async Task<IActionResult> OnPostResendAsync(CancellationToken cancellationToken)
    {
        var result = await accounts.ResendVerificationAsync(User.ActorId(), this.Origin(), cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ShowError(result.Error);
        }

        TempData[Notices.Success] = localizer["Verify.Resent"].Value;
        return RedirectToPage();
    }
}
