using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Features.Shared;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Account;

/// <summary>
/// Back from Google or GitHub (Features/Auth/ExternalAuth.cs) with who signed in there: a link from the settings is
/// made; otherwise the account it belongs to is signed in, or the person signs up with a username of their choosing.
/// What the provider said is dropped once it is used, or refused.
/// </summary>
public sealed class ExternalCompleteModel(AccountService accounts, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty] public string? Username { get; set; }

    [BindProperty] public string? InviteCode { get; set; }

    public ExternalIdentity Identity { get; private set; } = null!;

    public RegistrationMode Mode { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var external = await HttpContext.AuthenticateAsync(AuthSchemes.External);
        if (ExternalAuth.IdentityOf(external) is not { } identity)
        {
            return Expired();
        }

        if (external.Properties!.Items.TryGetValue(ExternalAuth.LinkItem, out var linkedFrom))
        {
            return await LinkAsync(identity, linkedFrom, cancellationToken);
        }

        if (User.IsHuman())
        {
            await HttpContext.SignOutAsync(AuthSchemes.External);
            return this.AlreadySignedIn(localizer);
        }

        var result = await accounts.ExternalSignInAsync(identity, cancellationToken);
        if (!result.IsSuccess)
        {
            return await RefusedAsync(result.Error, AuthSetup.LoginPath);
        }

        if (result.Value.Account is { } account)
        {
            await HttpContext.SignOutAsync(AuthSchemes.External);
            external.Properties.Items.TryGetValue(ExternalAuth.ReturnItem, out var returnUrl);
            return await LoginModel.SignInAfterFirstStepAsync(this, accounts, account, returnUrl, cancellationToken);
        }

        (Identity, Username) = (identity, result.Value.SuggestedUsername);
        Mode = await accounts.GetRegistrationModeAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (User.IsHuman())
        {
            return this.AlreadySignedIn(localizer);
        }

        var external = await HttpContext.AuthenticateAsync(AuthSchemes.External);
        if (ExternalAuth.IdentityOf(external) is not { } identity || external.Properties!.Items.ContainsKey(ExternalAuth.LinkItem))
        {
            return Expired();
        }

        var result = await accounts.RegisterExternalAsync(identity, Username, InviteCode, this.Origin(), cancellationToken);
        if (!result.IsSuccess)
        {
            // Only what the form can put right is shown on it; anything else (the address has an account after all,
            // sign-ups closed meanwhile) ends here.
            if (result.Error.Fields is null && result.Error.RetryAfter is null)
            {
                return await RefusedAsync(result.Error, AuthSetup.LoginPath);
            }

            Identity = identity;
            Mode = await accounts.GetRegistrationModeAsync(cancellationToken);
            return this.ShowError(result.Error);
        }

        await HttpContext.SignOutAsync(AuthSchemes.External);
        await HttpContext.SignInHumanAsync(result.Value);
        // Verified already: straight on to the first agent.
        return RedirectToPage("/Onboarding/Agent");
    }

    private async Task<IActionResult> LinkAsync(ExternalIdentity identity, string? linkedFrom, CancellationToken cancellationToken)
    {
        var back = SitePaths.SettingsSection(SettingsSections.Account);
        // Started by another account than the one signed in now (signed out and in meanwhile): nothing is linked.
        if (!User.IsMember() || User.ActorId().ToString("D") != linkedFrom)
        {
            await HttpContext.SignOutAsync(AuthSchemes.External);
            TempData[Notices.Error] = localizer["External.Failed", ExternalProviders.DisplayName(identity.Provider)].Value;
            return LocalRedirect(User.IsMember() ? back : AuthSetup.LoginPath);
        }

        var result = await accounts.LinkExternalAsync(User.ActorId(), identity, cancellationToken);
        if (!result.IsSuccess)
        {
            return await RefusedAsync(result.Error, back);
        }

        await HttpContext.SignOutAsync(AuthSchemes.External);
        TempData[Notices.Success] = localizer["Settings.Connected", ExternalProviders.DisplayName(identity.Provider)].Value;
        return LocalRedirect(back);
    }

    private async Task<IActionResult> RefusedAsync(Error error, string back)
    {
        await HttpContext.SignOutAsync(AuthSchemes.External);
        TempData[Notices.Error] = error.Detail ?? error.Code;
        return LocalRedirect(back);
    }

    /// <summary>No word from a provider, or it was too long ago (<see cref="ExternalAuth.Lifetime"/>): start again.</summary>
    private LocalRedirectResult Expired()
    {
        TempData[Notices.Error] = localizer["External.Expired"].Value;
        return LocalRedirect(User.IsHuman() ? SitePaths.Settings : AuthSetup.LoginPath);
    }
}
