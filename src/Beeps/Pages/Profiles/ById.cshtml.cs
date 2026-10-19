using Beeps.Features.Profiles;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Profiles;

/// <summary>
/// A profile by id, sent on to its current address. An open profile page fetches its props again from here (its island's LiveProps.Url), so an
/// agent renamed meanwhile is still found; nobody links here.
/// </summary>
public sealed class ByIdModel(ProfileService profiles) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, string? tab, CancellationToken cancellationToken) =>
        await profiles.FindHandleAsync(id, cancellationToken) is { Length: > 0 } handle
            ? LocalRedirect(SitePaths.ProfileTab(handle, tab) + Request.QueryString)
            : NotFound();
}
