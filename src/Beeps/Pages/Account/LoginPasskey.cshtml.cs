using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Account;

/// <summary>
/// The second step of signing in to an account that keeps its passkeys for it (PLAN 6.14): after the password, a code or
/// Google and GitHub, one of the account's passkeys. That the first step was passed is carried in TempData
/// (<see cref="FirstStepKey"/>), kept until signed in, so a passkey that did not work can be tried again.
/// </summary>
public sealed class LoginPasskeyModel(AccountService accounts, IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>What <see cref="AccountService.StartSecondStepAsync"/> gave.</summary>
    public const string FirstStepKey = "LoginFirstStep";

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    [BindProperty] public string? Credential { get; set; }

    [BindProperty] public string? Ticket { get; set; }

    private string? FirstStep => TempData.Peek(FirstStepKey) as string;

    public IActionResult OnGet()
    {
        if (User.IsHuman())
        {
            return LocalRedirect("~/");
        }

        // Opened by itself, or the sign-in long done: there is no first step here to follow.
        return FirstStep is null ? RedirectToPage("/Account/Login", new { ReturnUrl }) : Page();
    }

    /// <summary>What the browser asks for: one of the account's passkeys (JSON).</summary>
    public async Task<IActionResult> OnPostPasskeyOptionsAsync(CancellationToken cancellationToken) =>
        this.PasskeyOptions(await accounts.BeginSecondStepAsync(FirstStep, cancellationToken));

    /// <summary>What the authenticator signed, which signs in.</summary>
    public async Task<IActionResult> OnPostPasskeyAsync(CancellationToken cancellationToken)
    {
        if (User.IsHuman())
        {
            return this.AlreadySignedIn(localizer);
        }

        var result = await accounts.FinishSecondStepAsync(FirstStep, Credential, Ticket, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ShowError(result.Error);
        }

        TempData.Remove(FirstStepKey);
        return await LoginModel.SignInAsync(this, result.Value, ReturnUrl);
    }
}
