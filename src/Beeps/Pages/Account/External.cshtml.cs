using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Account;

/// <summary>
/// The way out to Google or GitHub (Features/Auth/ExternalAuth.cs): to sign in or up from the login and sign-up pages,
/// or to link from the settings. A provider this site has no app for sends the browser back, saying so.
/// </summary>
public sealed class ExternalModel(IAuthenticationSchemeProvider schemes, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    /// <summary>Signing in or up: a link, so the signed-out pages that have one can still be cached.</summary>
    public async Task<IActionResult> OnGetAsync(string provider)
    {
        if (User.IsHuman())
        {
            return LocalRedirect("~/");
        }

        var properties = new AuthenticationProperties { RedirectUri = ExternalAuth.CompletePath };
        if (Url.IsLocalUrl(ReturnUrl))
        {
            properties.Items[ExternalAuth.ReturnItem] = ReturnUrl;
        }

        return await ChallengeAsync(provider, properties, AuthSetup.LoginPath);
    }

    /// <summary>Linking from the settings: only ever finished by the account it was started from.</summary>
    public async Task<IActionResult> OnPostLinkAsync(string provider)
    {
        if (!User.IsMember())
        {
            return User.IsHuman() ? Forbid() : Challenge();
        }

        var properties = new AuthenticationProperties { RedirectUri = ExternalAuth.CompletePath };
        properties.Items[ExternalAuth.LinkItem] = User.ActorId().ToString("D");
        return await ChallengeAsync(provider, properties, Account);
    }

    /// <summary>The provider's way back failed, or was cancelled there (<see cref="ExternalAuth"/>).</summary>
    public IActionResult OnGetFailed(string provider, bool link)
    {
        TempData[Notices.Error] = localizer["External.Failed", ExternalProviders.DisplayName(ExternalProviders.Find(provider)!)].Value;
        return LocalRedirect(link ? Account : AuthSetup.LoginPath);
    }

    private static string Account => SitePaths.SettingsSection(SettingsSections.Account);

    /// <param name="provider">As the address has it, in any case: the route matches it ignoring case.</param>
    private async Task<IActionResult> ChallengeAsync(string provider, AuthenticationProperties properties, string back)
    {
        var scheme = ExternalProviders.Find(provider)!;
        if (await schemes.GetSchemeAsync(scheme) is null)
        {
            TempData[Notices.Error] = localizer["External.NotSetUp", ExternalProviders.DisplayName(scheme)].Value;
            return LocalRedirect(back);
        }

        return Challenge(properties, scheme);
    }
}
