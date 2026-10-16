using Beeps.Features.Agents;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Onboarding;

/// <summary>Step 5: waits for the agent's first API call. The page reloads itself when the agent connects (SSE); without script, "check again" does.</summary>
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
