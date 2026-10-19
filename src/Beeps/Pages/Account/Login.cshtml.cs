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

    /// <summary>What the authenticator signed, as the browser's JSON (the passkey button).</summary>
    [BindProperty] public string? Credential { get; set; }

    /// <summary>What the passkey options came with, sent back with the answer.</summary>
    [BindProperty] public string? Ticket { get; set; }

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

        return await SignInAfterFirstStepAsync(this, accounts, account, ReturnUrl, cancellationToken);
    }

    /// <summary>The passkey button, first: what the browser asks for, any passkey the person has here (JSON).</summary>
    public async Task<IActionResult> OnPostPasskeyOptionsAsync() => this.PasskeyOptions(await accounts.BeginPasskeySignInAsync());

    /// <summary>Then what the authenticator signed, which signs in to its account without a password.</summary>
    public async Task<IActionResult> OnPostPasskeyAsync(CancellationToken cancellationToken)
    {
        if (User.IsHuman())
        {
            return this.AlreadySignedIn(localizer);
        }

        var result = await accounts.PasskeySignInAsync(Credential, Ticket, this.Origin(), cancellationToken);
        return result.IsSuccess ? await SignInAsync(this, result.Value, ReturnUrl) : this.ShowError(result.Error);
    }

    /// <summary>
    /// The first step of signing in is done (the password, a code, Google or GitHub): on to the account's passkey, if
    /// it keeps them for the second step (PLAN 6.14), or signed in.
    /// </summary>
    internal static async Task<IActionResult> SignInAfterFirstStepAsync(
        PageModel page, AccountService accounts, AccountState account, string? returnUrl, CancellationToken cancellationToken)
    {
        if (await accounts.StartSecondStepAsync(account.UserId, cancellationToken) is { } firstStep)
        {
            page.TempData[LoginPasskeyModel.FirstStepKey] = firstStep;
            return page.RedirectToPage("/Account/LoginPasskey", new { returnUrl });
        }

        return await SignInAsync(page, account, returnUrl);
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
