using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Features.Localization;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages;

/// <summary>The account's own settings: profile, language and password (PLAN 2.2, 6.12).</summary>
public sealed class SettingsModel(AccountService accounts, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public AccountSettings Settings { get; private set; } = null!;

    [BindProperty] public string? DisplayName { get; set; }

    [BindProperty] public string? Bio { get; set; }

    [BindProperty] public string? Culture { get; set; }

    [BindProperty] public string? CurrentPassword { get; set; }

    [BindProperty] public string? NewPassword { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        (DisplayName, Bio) = (Settings.DisplayName, Settings.Bio);
        return Page();
    }

    public async Task<IActionResult> OnPostProfileAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await accounts.UpdateProfileAsync(User.ActorId(), DisplayName, Bio, cancellationToken), "Settings.ProfileSaved", cancellationToken);

    // The page then simply shows up in the new language; no notice needed.
    public async Task<IActionResult> OnPostLanguageAsync(CancellationToken cancellationToken)
    {
        var result = await accounts.SetLocaleAsync(User.ActorId(), Culture, cancellationToken);
        if (result.IsSuccess)
        {
            CultureCookie.Append(Response, SupportedCultures.Find(Culture)!);
        }

        return await DoneAsync(result, notice: null, cancellationToken);
    }

    public async Task<IActionResult> OnPostPasswordAsync(CancellationToken cancellationToken) =>
        await DoneAsync(
            await accounts.ChangePasswordAsync(User.ActorId(), User.SessionId(), CurrentPassword, NewPassword, cancellationToken),
            "Settings.PasswordChanged",
            cancellationToken);

    private async Task<IActionResult> DoneAsync(Result result, string? notice, CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            await LoadAsync(cancellationToken);
            return this.ShowError(result.Error);
        }

        if (notice is not null)
        {
            TempData[Notices.Success] = localizer[notice].Value;
        }

        return RedirectToPage();
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await accounts.GetSettingsAsync(User.ActorId(), cancellationToken);
        if (settings.IsSuccess)
        {
            Settings = settings.Value;
        }

        return settings.IsSuccess;
    }
}
