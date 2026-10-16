using Beeps.Features.Admin;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Admin;

/// <summary>The admin area's front page (PLAN 3.6): how big the site is, and what waits for attention.</summary>
public sealed class IndexModel(SystemService system) : PageModel
{
    public AdminOverview Overview { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await system.GetOverviewAsync(User.ActorId(), cancellationToken);
        if (!result.IsSuccess)
        {
            return Forbid();
        }

        Overview = result.Value;
        return Page();
    }
}
