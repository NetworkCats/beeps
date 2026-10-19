using Beeps.Features.Agents;
using Beeps.Features.Auth;
using Beeps.Features.Shared;
using Beeps.Pages.Islands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Agents;

/// <summary>The owner's agents: create, regenerate the key, disable / enable, delete (PLAN 3.5).</summary>
[AppShell]
public sealed class IndexModel(AgentService agents, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public IReadOnlyList<AgentSummary> Agents { get; private set; } = [];

    /// <summary>Set only in the response that generated it.</summary>
    public AgentWithKey? NewKey { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Agents = await agents.ListAsync(User.ActorId(), cancellationToken);

    public Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken) =>
        ShowKeyAsync(agents.CreateAsync(User.ActorId(), cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostRegenerateAsync(Guid id, CancellationToken cancellationToken) =>
        ShowKeyAsync(agents.RegenerateKeyAsync(User.ActorId(), id, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostDisableAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await agents.DisableAsync(User.ActorId(), id, cancellationToken), "Agents.Disabled");

    public async Task<IActionResult> OnPostEnableAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await agents.EnableAsync(User.ActorId(), id, cancellationToken), "Agents.Enabled");

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await agents.DeleteAsync(User.ActorId(), id, cancellationToken), "Agents.Deleted");

    private async Task<IActionResult> ShowKeyAsync(Task<Result<AgentWithKey>> action, CancellationToken cancellationToken)
    {
        var result = await action;
        Agents = await agents.ListAsync(User.ActorId(), cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ShowError(result.Error);
        }

        NewKey = result.Value;
        return Page();
    }

    private RedirectToPageResult Done(Result result, string notice)
    {
        this.Notify(result, localizer[notice]);
        return RedirectToPage();
    }
}
