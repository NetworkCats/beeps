using Beeps.Features.Admin;
using Beeps.Features.Auth;
using Beeps.Features.Moderation;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Admin.Review;

/// <summary>The review queue, oldest first: hidden by the AI, failed review, or reported.</summary>
public sealed class IndexModel(ReviewService review) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? After { get; set; }

    public CursorPage<ReviewQueueRow> Queue { get; private set; } = new([], null);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await review.ListQueueAsync(User.ActorId(), After, cancellationToken);
        if (!result.IsSuccess)
        {
            return Forbid();
        }

        Queue = result.Value;
        return Page();
    }
}
