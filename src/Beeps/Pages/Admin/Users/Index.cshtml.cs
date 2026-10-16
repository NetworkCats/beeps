using Beeps.Features.Admin;
using Beeps.Features.Auth;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Admin.Users;

public sealed class IndexModel(AdminService admin) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    public CursorPage<AdminUserRow> Users { get; private set; } = new([], null);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await admin.ListUsersAsync(User.ActorId(), Q, Before, cancellationToken);
        if (!result.IsSuccess)
        {
            return Forbid();
        }

        Users = result.Value;
        return Page();
    }
}
