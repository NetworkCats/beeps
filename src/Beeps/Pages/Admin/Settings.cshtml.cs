using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Auth;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Admin;

/// <summary>Registration mode and invite codes.</summary>
public sealed class SettingsModel(AdminService admin, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty] public RegistrationMode Mode { get; set; }

    public IReadOnlyList<InviteRow> Invites { get; private set; } = [];

    /// <summary>Set only in the response that created it.</summary>
    public CreatedInvite? NewInvite { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken) ? Page() : Forbid();

    // Posted without a mode, or with one that is none, it would bind to the first: registration open to all.
    public async Task<IActionResult> OnPostModeAsync(CancellationToken cancellationToken) =>
        ModelState.IsValid && Request.Form.ContainsKey(nameof(Mode))
            ? Done(await admin.SetRegistrationModeAsync(User.ActorId(), Mode, cancellationToken), "Admin.Done.Mode")
            : BadRequest();

    public async Task<IActionResult> OnPostCreateInviteAsync(CancellationToken cancellationToken)
    {
        var result = await admin.CreateInviteAsync(User.ActorId(), cancellationToken);
        if (!result.IsSuccess)
        {
            return Forbid();
        }

        NewInvite = result.Value;
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeInviteAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await admin.RevokeInviteAsync(User.ActorId(), id, cancellationToken), "Admin.Done.InviteRevoked");

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var mode = await admin.GetRegistrationModeAsync(User.ActorId(), cancellationToken);
        var invites = await admin.ListInvitesAsync(User.ActorId(), cancellationToken);
        if (!mode.IsSuccess || !invites.IsSuccess)
        {
            return false;
        }

        Mode = mode.Value;
        Invites = invites.Value;
        return true;
    }

    private RedirectToPageResult Done(Result result, string notice)
    {
        this.Notify(result, localizer[notice]);
        return RedirectToPage();
    }
}
