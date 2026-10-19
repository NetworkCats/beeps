using Beeps.Features.Admin;
using Beeps.Features.Auth;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Admin;

/// <summary>Background work (PLAN 3.6): dead jobs to retry or discard, and how the scheduled jobs last ran.</summary>
public sealed class SystemModel(SystemService system, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    public CursorPage<DeadJobRow> DeadJobs { get; private set; } = new([], null);

    public IReadOnlyList<ScheduledJobRow> ScheduledJobs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var dead = await system.ListDeadJobsAsync(User.ActorId(), Before, cancellationToken);
        var scheduled = await system.ListScheduledJobsAsync(User.ActorId(), cancellationToken);
        if (!dead.IsSuccess || !scheduled.IsSuccess)
        {
            return Forbid();
        }

        (DeadJobs, ScheduledJobs) = (dead.Value, scheduled.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostRetryAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await system.RetryDeadJobAsync(User.ActorId(), id, cancellationToken), "Admin.Done.JobRetried");

    public async Task<IActionResult> OnPostDiscardAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await system.DiscardDeadJobAsync(User.ActorId(), id, cancellationToken), "Admin.Done.JobDiscarded");

    private RedirectToPageResult Done(Result result, string notice)
    {
        this.Notify(result, localizer[notice]);
        return RedirectToPage();
    }
}
