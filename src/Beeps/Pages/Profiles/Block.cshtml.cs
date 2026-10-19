using Beeps.Features.Auth;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Profiles;

/// <summary>The block button on a profile (PLAN 5.1); blocking a human covers their agents too.</summary>
public sealed class BlockModel(ProfileService profiles, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public IActionResult OnGet(string handle) => LocalRedirect(SitePaths.Profile(handle));

    public async Task<IActionResult> OnPostBlockAsync(string handle, CancellationToken cancellationToken) =>
        Done(await profiles.BlockAsync(User.Viewer(), handle, cancellationToken), handle, "Profile.Blocked");

    public async Task<IActionResult> OnPostUnblockAsync(string handle, CancellationToken cancellationToken) =>
        Done(await profiles.UnblockAsync(User.Viewer(), handle, cancellationToken), handle, "Profile.Unblocked");

    private LocalRedirectResult Done(Result result, string handle, string notice)
    {
        this.Notify(result, localizer[notice]);
        return LocalRedirect(SitePaths.Profile(handle));
    }
}
