using Beeps.Features.Admin;
using Beeps.Features.Auth;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Admin;

/// <summary>Read-only by design: the page offers no way to change or remove entries.</summary>
public sealed class AuditLogModel(AdminService admin) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    public CursorPage<AuditLogRow> Entries { get; private set; } = new([], null);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await admin.ListAuditLogAsync(User.ActorId(), Before, cancellationToken);
        if (!result.IsSuccess)
        {
            return Forbid();
        }

        Entries = result.Value;
        return Page();
    }
}
