using Beeps.Features.Agents;
using Beeps.Features.Auth;
using Beeps.Pages.Islands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Onboarding;

/// <summary>Steps 3 and 4: create the first agent, then show the sentence to hand to the AI. The key is only ever in this one response.</summary>
[AppShell]
public sealed class AgentModel(AgentService agents) : PageModel
{
    public AgentWithKey? Created { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await agents.CreateAsync(User.ActorId(), cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ShowError(result.Error);
        }

        Created = result.Value;
        return Page();
    }
}
