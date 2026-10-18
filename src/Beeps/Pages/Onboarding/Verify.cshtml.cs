using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Onboarding;

/// <summary>Entering the code sent at sign-up; or asking for another one, or for it to go to another address.</summary>
public sealed class VerifyModel(AccountService accounts, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public Verification Verification { get; private set; } = null!;

    /// <summary>The form for another address was sent and refused, so it stays open.</summary>
    public bool Changing { get; private set; }

    [BindProperty] public string? Code { get; set; }

    [BindProperty] public string? NewEmail { get; set; }

    [BindProperty] public string? CurrentPassword { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (User.FindFirst(BeepsClaims.EmailVerified)?.Value == "true")
        {
            return RedirectToPage("/Onboarding/Agent");
        }

        return await LoadAsync(cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await accounts.VerifyEmailAsync(User.ActorId(), Code, cancellationToken);
        return result.IsSuccess ? RedirectToPage("/Onboarding/Agent") : await RefusedAsync(result.Error, cancellationToken);
    }

    public async Task<IActionResult> OnPostResendAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await accounts.ResendVerificationAsync(User.ActorId(), this.Origin(), cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostEmailAsync(CancellationToken cancellationToken)
    {
        var result = await accounts.ChangeUnverifiedEmailAsync(User.ActorId(), NewEmail, CurrentPassword, this.Origin(), cancellationToken);
        Changing = !result.IsSuccess;
        return await DoneAsync(result, cancellationToken);
    }

    // Either way a new code is on its way, to the address the page then shows.
    private async Task<IActionResult> DoneAsync(Result result, CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            return await RefusedAsync(result.Error, cancellationToken);
        }

        TempData[Notices.Success] = localizer["Verify.Resent"].Value;
        return RedirectToPage();
    }

    private async Task<IActionResult> RefusedAsync(Error error, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return this.ShowError(error);
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var verification = await accounts.GetVerificationAsync(User.ActorId(), cancellationToken);
        if (verification.IsSuccess)
        {
            Verification = verification.Value;
        }

        return verification.IsSuccess;
    }
}
