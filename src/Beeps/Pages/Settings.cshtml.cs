using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Features.Avatars;
using Beeps.Features.Media;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Site;
using Beeps.Pages.Islands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages;

/// <summary>
/// The account's own settings: avatar, banner, profile, privacy, language, colour scheme, email, password, passkeys, the
/// Google and GitHub accounts it signs in with and the devices signed in (PLAN 2.2, 3.4, 5.2, 6.12, 6.14). As on
/// Twitter, <c>/settings</c> lists the sections and each has a page of its own (<see cref="SettingsSections"/>); a form
/// comes back to the section it belongs to, whichever address it was sent to.
/// </summary>
[RequestSizeLimit(MediaRules.MaxImageRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MediaRules.MaxImageRequestBytes, MemoryBufferThreshold = MediaRules.MaxImageRequestBytes)]
[AppShell]
public sealed class SettingsModel(
    AccountService accounts,
    SessionService sessions,
    AvatarService avatars,
    BannerService banners,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    public AccountSettings Settings { get; private set; } = null!;

    /// <summary>The devices signed in; only on their page.</summary>
    public IReadOnlyList<SignedInDevice>? Devices { get; private set; }

    /// <summary>The account's passkeys; only on their page.</summary>
    public IReadOnlyList<PasskeySummary>? Passkeys { get; private set; }

    public Uri? BannerUrl { get; private set; }

    /// <summary>The section shown; none for the list of them.</summary>
    public string? Section { get; private set; }

    [BindProperty] public string? DisplayName { get; set; }

    [BindProperty] public string? Bio { get; set; }

    [BindProperty] public string? Website { get; set; }

    [BindProperty] public string? Culture { get; set; }

    [BindProperty] public string? Theme { get; set; }

    [BindProperty] public string? CurrentPassword { get; set; }

    [BindProperty] public string? NewPassword { get; set; }

    [BindProperty] public string? NewEmail { get; set; }

    [BindProperty] public string? Code { get; set; }

    /// <summary>The provider to unlink (<see cref="ExternalProviders"/>).</summary>
    [BindProperty] public string? Provider { get; set; }

    /// <summary>The device to sign out.</summary>
    [BindProperty] public Guid SessionId { get; set; }

    /// <summary>What the authenticator made, as the browser's JSON, and the ticket its options came with.</summary>
    [BindProperty] public string? Credential { get; set; }

    [BindProperty] public string? Ticket { get; set; }

    /// <summary>The passkey to remove.</summary>
    [BindProperty] public Guid PasskeyId { get; set; }

    /// <summary>Passkeys as the second step after the password, instead of a way to sign in by themselves.</summary>
    [BindProperty] public bool TwoFactor { get; set; }

    [BindProperty] public bool HideLikes { get; set; }

    [BindProperty] public bool HideFollows { get; set; }

    [BindProperty] public bool HideFollowCounts { get; set; }

    [BindProperty] public IFormFile? Avatar { get; set; }

    [BindProperty] public AvatarCropForm Crop { get; set; } = new();

    [BindProperty] public IFormFile? Banner { get; set; }

    [BindProperty] public BannerCropForm BannerCrop { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string? section, CancellationToken cancellationToken)
    {
        // The route takes the section in any case: one address for each.
        if (section?.ToLowerInvariant() is { } lower && lower != section)
        {
            return LocalRedirect(SitePaths.SettingsSection(lower));
        }

        Section = section;
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        (DisplayName, Bio, Website) = (Settings.DisplayName, Settings.Bio, Settings.Website);
        return Page();
    }

    public async Task<IActionResult> OnPostAvatarAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await avatars.UploadAsync(User.Viewer(), User.ActorId(), Avatar.ToMediaContent(), Crop.ToCrop(), cancellationToken), SettingsSections.Profile, "Avatar.Saved", cancellationToken);

    public async Task<IActionResult> OnPostRemoveAvatarAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await avatars.RemoveAsync(User.Viewer(), User.ActorId(), cancellationToken), SettingsSections.Profile, "Avatar.Removed", cancellationToken);

    public async Task<IActionResult> OnPostBannerAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await banners.UploadAsync(User.Viewer(), User.ActorId(), Banner.ToMediaContent(), BannerCrop.ToCrop(), cancellationToken), SettingsSections.Profile, "Banner.Saved", cancellationToken);

    public async Task<IActionResult> OnPostRemoveBannerAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await banners.RemoveAsync(User.Viewer(), User.ActorId(), cancellationToken), SettingsSections.Profile, "Banner.Removed", cancellationToken);

    public async Task<IActionResult> OnPostProfileAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await accounts.UpdateProfileAsync(User.ActorId(), DisplayName, Bio, Website, cancellationToken), SettingsSections.Profile, "Settings.ProfileSaved", cancellationToken);

    // Each switch is sent as it is flipped, and stays as it was flipped: no notice needed.
    public async Task<IActionResult> OnPostPrivacyAsync(CancellationToken cancellationToken) =>
        await DoneAsync(
            await accounts.SetPrivacyAsync(User.ActorId(), new ProfilePrivacy(HideLikes, HideFollows, HideFollowCounts), cancellationToken),
            SettingsSections.Privacy,
            notice: null,
            cancellationToken);

    // The page then simply shows up in the new language; no notice needed.
    public async Task<IActionResult> OnPostLanguageAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await this.ChooseCultureAsync(accounts, Culture, cancellationToken), SettingsSections.Display, notice: null, cancellationToken);

    // The page then simply shows up in the new scheme, as do the account's other tabs.
    public async Task<IActionResult> OnPostThemeAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await this.ChooseThemeAsync(accounts, Theme, cancellationToken), SettingsSections.Display, notice: null, cancellationToken);

    // Sent without the current password only by an account that has none, which this sets the first of.
    public async Task<IActionResult> OnPostPasswordAsync(CancellationToken cancellationToken) =>
        await DoneAsync(
            await accounts.ChangePasswordAsync(User.ActorId(), User.SessionId(), CurrentPassword, NewPassword, cancellationToken),
            SettingsSections.Password,
            string.IsNullOrEmpty(CurrentPassword) ? "Settings.PasswordSet" : "Settings.PasswordChanged",
            cancellationToken);

    // Linking goes out to the provider and back (Pages/Account/External); unlinking is done here.
    public async Task<IActionResult> OnPostUnlinkAsync(CancellationToken cancellationToken)
    {
        var result = await accounts.UnlinkExternalAsync(User.ActorId(), Provider, cancellationToken);
        if (result.IsSuccess && ExternalProviders.Find(Provider) is { } provider)
        {
            TempData[Notices.Success] = localizer["Settings.Disconnected", ExternalProviders.DisplayName(provider)].Value;
        }

        return await DoneAsync(result, SettingsSections.Account, notice: null, cancellationToken);
    }

    // Adding a passkey is two requests: the options for the browser (JSON), then what the authenticator made.
    public async Task<IActionResult> OnPostPasskeyOptionsAsync(CancellationToken cancellationToken) =>
        this.PasskeyOptions(await accounts.BeginAddPasskeyAsync(User.ActorId(), cancellationToken));

    public async Task<IActionResult> OnPostAddPasskeyAsync(CancellationToken cancellationToken) =>
        await DoneAsync(
            await accounts.AddPasskeyAsync(User.ActorId(), Credential, Ticket, Request.Headers.UserAgent.ToString(), cancellationToken),
            SettingsSections.Passkeys,
            "Passkeys.Added",
            cancellationToken);

    public async Task<IActionResult> OnPostRemovePasskeyAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await accounts.RemovePasskeyAsync(User.ActorId(), PasskeyId, cancellationToken), SettingsSections.Passkeys, "Passkeys.Removed", cancellationToken);

    // A switch, sent as it is flipped, and staying as it was flipped: no notice needed.
    public async Task<IActionResult> OnPostPasskeyTwoFactorAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await accounts.SetPasskeyTwoFactorAsync(User.ActorId(), TwoFactor, cancellationToken), SettingsSections.Passkeys, notice: null, cancellationToken);

    // The page then asks for the code sent to the new address.
    public async Task<IActionResult> OnPostEmailAsync(CancellationToken cancellationToken) =>
        await DoneAsync(
            await accounts.RequestEmailChangeAsync(User.ActorId(), NewEmail, CurrentPassword, this.Origin(), cancellationToken),
            SettingsSections.Email,
            notice: null,
            cancellationToken);

    // Done, back to the account, which shows the new address.
    public async Task<IActionResult> OnPostConfirmEmailAsync(CancellationToken cancellationToken)
    {
        var result = await accounts.ConfirmEmailChangeAsync(User.ActorId(), Code, cancellationToken);
        return await DoneAsync(result, result.IsSuccess ? SettingsSections.Account : SettingsSections.Email, "Settings.EmailChanged", cancellationToken);
    }

    public async Task<IActionResult> OnPostResendEmailAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await accounts.ResendEmailChangeAsync(User.ActorId(), this.Origin(), cancellationToken), SettingsSections.Email, "Verify.Resent", cancellationToken);

    public async Task<IActionResult> OnPostCancelEmailAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await accounts.CancelEmailChangeAsync(User.ActorId(), cancellationToken), SettingsSections.Email, notice: null, cancellationToken);

    public async Task<IActionResult> OnPostEndSessionAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await sessions.EndDeviceAsync(User.ActorId(), User.SessionId(), SessionId, cancellationToken), SettingsSections.Sessions, "Sessions.Ended", cancellationToken);

    public async Task<IActionResult> OnPostEndOtherSessionsAsync(CancellationToken cancellationToken) =>
        await DoneAsync(await sessions.EndOtherDevicesAsync(User.ActorId(), User.SessionId(), cancellationToken), SettingsSections.Sessions, "Sessions.EndedOthers", cancellationToken);

    private async Task<IActionResult> DoneAsync(Result result, string section, string? notice, CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            Section = section;
            var loaded = await LoadAsync(cancellationToken);
            // Only the profile form posts the name fields; any other refused form (an avatar too small, say) leaves
            // them as saved, or they would show empty and an untouched bio be saved blank next time.
            if (loaded && (!Request.HasFormContentType || !Request.Form.ContainsKey(nameof(DisplayName))))
            {
                (DisplayName, Bio, Website) = (Settings.DisplayName, Settings.Bio, Settings.Website);
            }

            return this.ShowError(result.Error);
        }

        if (notice is not null)
        {
            TempData[Notices.Success] = localizer[notice].Value;
        }

        return RedirectToPage(new { section });
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await accounts.GetSettingsAsync(User.ActorId(), cancellationToken);
        if (settings.IsSuccess)
        {
            Settings = settings.Value;
            BannerUrl = await banners.GetUrlAsync(User.ActorId(), cancellationToken);
            if (Section == SettingsSections.Sessions)
            {
                Devices = await sessions.ListDevicesAsync(User.ActorId(), User.SessionId(), cancellationToken);
            }

            if (Section == SettingsSections.Passkeys)
            {
                Passkeys = await accounts.ListPasskeysAsync(User.ActorId(), cancellationToken);
            }
        }

        return settings.IsSuccess;
    }
}
