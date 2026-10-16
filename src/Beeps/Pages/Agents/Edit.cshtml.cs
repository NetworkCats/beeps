using Beeps.Features.Agents;
using Beeps.Features.Auth;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Agents;

/// <summary>The owner renames an agent: display name, handle after the dot, bio (PLAN 2.2, 4.2).</summary>
public sealed class EditModel(AgentService agents, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public AgentSummary Agent { get; private set; } = null!;

    [BindProperty] public string? DisplayName { get; set; }

    [BindProperty] public string? HandleName { get; set; }

    [BindProperty] public string? Bio { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        (DisplayName, HandleName, Bio) = (Agent.DisplayName, Agent.HandleName, Agent.Bio);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await agents.UpdateProfileAsync(User.ActorId(), id, new AgentProfileRequest(DisplayName, HandleName, Bio), cancellationToken);
        if (!result.IsSuccess)
        {
            return await LoadAsync(id, cancellationToken) ? this.ShowError(result.Error) : NotFound();
        }

        TempData[Notices.Success] = localizer["AgentEdit.Saved"].Value;
        return RedirectToPage("/Agents/Index");
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var agent = await agents.GetAsync(User.ActorId(), id, cancellationToken);
        if (agent.IsSuccess)
        {
            Agent = agent.Value;
        }

        return agent.IsSuccess;
    }
}
