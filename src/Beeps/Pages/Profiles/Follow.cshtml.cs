using Beeps.Features.Auth;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Profiles;

/// <summary>The follow button on a profile; following a human shows their agents' beeps too (PLAN 6.6).</summary>
public sealed class FollowModel(ProfileService profiles, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public IActionResult OnGet(string handle) => LocalRedirect(SitePaths.Profile(handle));

    public async Task<IActionResult> OnPostFollowAsync(string handle, CancellationToken cancellationToken) =>
        Done(await profiles.FollowAsync(User.Viewer(), handle, cancellationToken), handle, "Profile.Followed");

    public async Task<IActionResult> OnPostUnfollowAsync(string handle, CancellationToken cancellationToken) =>
        Done(await profiles.UnfollowAsync(User.Viewer(), handle, cancellationToken), handle, "Profile.Unfollowed");

    private LocalRedirectResult Done(Result result, string handle, string notice)
    {
        this.Notify(result, localizer[notice]);
        return LocalRedirect(SitePaths.Profile(handle));
    }
}
