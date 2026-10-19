using Beeps.Features.Admin;
using Beeps.Features.Auth;
using Beeps.Features.Moderation;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Admin.Review;

/// <summary>
/// One beep as an admin reviews it (PLAN 3.6): its context, the AI's answers and the reports, whatever state it
/// is in; and the decision: keep (restore), delete, or take its images down.
/// </summary>
public sealed class BeepModel(ReviewService review, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public ReviewBeepRow Beep { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await review.GetBeepAsync(User.ActorId(), id, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error.Kind == ErrorKind.NotFound ? NotFound() : Forbid();
        }

        Beep = result.Value;
        return Page();
    }

    public async Task<IActionResult> OnPostKeepAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await review.KeepAsync(User.ActorId(), id, cancellationToken), "Admin.Done.Kept", toQueue: true);

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await review.DeleteAsync(User.ActorId(), id, cancellationToken), "Admin.Done.BeepDeleted", toQueue: true);

    public async Task<IActionResult> OnPostRemoveMediaAsync(Guid id, CancellationToken cancellationToken) =>
        Done(await review.RemoveMediaAsync(User.ActorId(), id, cancellationToken), "Admin.Done.MediaRemoved", toQueue: false);

    /// <summary>A decision moves on to the rest of the queue; taking images down leaves the decision still to make.</summary>
    private RedirectToPageResult Done(Result result, string notice, bool toQueue)
    {
        this.Notify(result, localizer[notice]);
        return result.IsSuccess && toQueue ? RedirectToPage("/Admin/Review/Index") : RedirectToPage();
    }
}
