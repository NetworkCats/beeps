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
/// The account's own settings: avatar, banner, profile, privacy, language, colour scheme, email, password and the Google
/// and GitHub accounts it signs in with (PLAN 2.2, 3.4, 5.2, 6.12, 6.14). As on
/// Twitter, <c>/settings</c> lists the sections and each has a page of its own (<see cref="SettingsSections"/>); a form
/// comes back to the section it belongs to, whichever address it was sent to.
/// </summary>
[RequestSizeLimit(MediaRules.MaxImageRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MediaRules.MaxImageRequestBytes, MemoryBufferThreshold = MediaRules.MaxImageRequestBytes)]
[AppShell]
public sealed class SettingsModel(AccountService accounts, AvatarService avatars, BannerService banners, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public AccountSettings Settings { get; private set; } = null!;

    public Uri? BannerUrl { get; private set; }

    /// <summary>The section shown; none for the list of them.</summary>
    public string? Section { get; private set; }

    [BindProperty] public string? DisplayName { get; set; }

    [BindProperty] public string? Bio { get; set; }

    [BindProperty] public string? Culture { get; set; }

    [BindProperty] public string? Theme { get; set; }

    [BindProperty] public string? CurrentPassword { get; set; }

    [BindProperty] public string? NewPassword { get; set; }

    [BindProperty] public string? NewEmail { get; set; }

    [BindProperty] public string? Code { get; set; }

    /// <summary>The provider to unlink (<see cref="ExternalProviders"/>).</summary>
    [BindProperty] public string? Provider { get; set; }

    [BindProperty] public bool HideLikes { get; set; }

    [BindProperty] public bool HideFollows { get; set; }

    [BindProperty] public bool HideFollowCounts { get; set; }

    [BindProperty] public IFormFile? Avatar { get; set; }

    [BindProperty] public AvatarCropForm Crop { get; set; } = new();

    [BindProperty] public IFormFile? Banner { get; set; }

    [BindProperty] public BannerCropForm BannerCrop { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string? section, CancellationToken cancellationToken)
    {
        Section = section;
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        (DisplayName, Bio) = (Settings.DisplayName, Settings.Bio);
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
        await DoneAsync(await accounts.UpdateProfileAsync(User.ActorId(), DisplayName, Bio, cancellationToken), SettingsSections.Profile, "Settings.ProfileSaved", cancellationToken);

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

    private async Task<IActionResult> DoneAsync(Result result, string section, string? notice, CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            Section = section;
            await LoadAsync(cancellationToken);
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
        }

        return settings.IsSuccess;
    }
}
