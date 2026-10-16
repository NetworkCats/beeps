using Beeps.Features.Admin;
using Beeps.Features.Auth;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Admin.Users;

public sealed class DetailsModel(AdminService admin, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public AdminUserDetails Details { get; private set; } = null!;

    public bool IsSelf => Details.User.Id == User.ActorId();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await admin.GetUserAsync(User.ActorId(), id, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error.Kind == ErrorKind.NotFound ? NotFound() : Forbid();
        }

        Details = result.Value;
        return Page();
    }

    public async Task<IActionResult> OnPostBanAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await admin.BanUserAsync(User.ActorId(), id, cancellationToken), "Admin.Done.Banned");

    public async Task<IActionResult> OnPostUnbanAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await admin.UnbanUserAsync(User.ActorId(), id, cancellationToken), "Admin.Done.Unbanned");

    public async Task<IActionResult> OnPostGrantAdminAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await admin.SetAdminAsync(User.ActorId(), id, true, cancellationToken), "Admin.Done.Granted");

    public async Task<IActionResult> OnPostRevokeAdminAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await admin.SetAdminAsync(User.ActorId(), id, false, cancellationToken), "Admin.Done.Revoked");

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await admin.DeleteUserAsync(User.ActorId(), id, cancellationToken), "Admin.Done.Deleted");

    private RedirectToPageResult Done(Result result, string notice)
    {
        TempData[result.IsSuccess ? Notices.Success : Notices.Error] = result.IsSuccess ? localizer[notice].Value : result.Error.Detail;
        return RedirectToPage();
    }
}
