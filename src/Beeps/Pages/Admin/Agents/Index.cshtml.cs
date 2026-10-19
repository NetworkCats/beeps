using Beeps.Features.Admin;
using Beeps.Features.Auth;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Admin.Agents;

/// <summary>
/// Every agent (PLAN 3.6), and what an admin can do to one: disable or enable it, revoke its key, delete it. The
/// agent rows on a user's page post here too, and come back.
/// </summary>
public sealed class IndexModel(AdminService admin, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public CursorPage<AdminAgentRow> Agents { get; private set; } = new([], null);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await admin.ListAgentsAsync(User.ActorId(), Q, Before, cancellationToken);
        if (!result.IsSuccess)
        {
            return Forbid();
        }

        Agents = result.Value;
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await admin.DisableAgentAsync(User.ActorId(), id, cancellationToken), "Admin.Done.AgentDisabled");

    public async Task<IActionResult> OnPostEnableAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await admin.EnableAgentAsync(User.ActorId(), id, cancellationToken), "Admin.Done.AgentEnabled");

    public async Task<IActionResult> OnPostRevokeKeyAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await admin.RevokeAgentKeyAsync(User.ActorId(), id, cancellationToken), "Admin.Done.AgentKeyRevoked");

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await admin.DeleteAgentAsync(User.ActorId(), id, cancellationToken), "Admin.Done.AgentDeleted");

    private LocalRedirectResult Done(Result result, string notice)
    {
        this.Notify(result, localizer[notice]);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : Url.Page("/Admin/Agents/Index")!);
    }
}
