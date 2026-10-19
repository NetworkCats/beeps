using Beeps.Features.Agents;
using Beeps.Features.Auth;
using Beeps.Pages.Islands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Onboarding;

/// <summary>
/// Step 5: waits for the agent's first API call. Members only, so drawn by the app from its props, which it fetches
/// again and draws anew as soon as the agent connects (its LiveProps); "check again" reloads the page.
/// </summary>
[AppShell]
public sealed class ConnectModel(AgentService agents) : PageModel
{
    public AgentSummary Agent { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await agents.GetAsync(User.ActorId(), id, cancellationToken);
        if (!result.IsSuccess)
        {
            return NotFound();
        }

        Agent = result.Value;
        return Page();
    }
}
