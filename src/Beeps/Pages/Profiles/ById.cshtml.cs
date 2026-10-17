using Beeps.Features.Profiles;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Profiles;

/// <summary>
/// A profile by id, sent on to its current address. An open profile page draws itself again from here (live.ts), so an
/// agent renamed meanwhile is still found; nobody links here.
/// </summary>
public sealed class ByIdModel(IProfileStore profiles) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, string? tab, CancellationToken cancellationToken) =>
        await profiles.FindHandleAsync(id, cancellationToken) is { Length: > 0 } handle
            ? LocalRedirect((tab is null ? SitePaths.Profile(handle) : SitePaths.ProfileReplies(handle)) + Request.QueryString)
            : NotFound();
}
