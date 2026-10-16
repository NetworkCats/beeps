using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Account;

public sealed class RegisterModel(AccountService accounts) : PageModel
{
    [BindProperty] public string? Email { get; set; }

    [BindProperty] public string? Username { get; set; }

    [BindProperty] public string? Password { get; set; }

    [BindProperty] public string? InviteCode { get; set; }

    [BindProperty(Name = "cf-turnstile-response")] public string? CaptchaToken { get; set; }

    public RegistrationMode Mode { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (User.IsHuman())
        {
            return LocalRedirect("~/");
        }

        Mode = await accounts.GetRegistrationModeAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await accounts.RegisterAsync(new RegisterRequest(Email, Username, Password, InviteCode, CaptchaToken), this.Origin(), cancellationToken);
        if (!result.IsSuccess)
        {
            Mode = await accounts.GetRegistrationModeAsync(cancellationToken);
            return this.ShowError(result.Error);
        }

        await HttpContext.SignInHumanAsync(result.Value);
        return RedirectToPage("/Onboarding/Verify");
    }
}
