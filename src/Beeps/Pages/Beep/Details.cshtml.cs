using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Pages.Beep;

/// <summary>A beep in its thread: the whole conversation from its first beep, oldest first, with this one marked.</summary>
[OutputCache(PolicyName = PublicPageCache.Policy)]
public sealed class DetailsModel(BeepService beeps) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? After { get; set; }

    public BeepView Focus { get; private set; } = null!;

    public CursorPage<BeepView> Thread { get; private set; } = new([], null);

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var viewer = User.OptionalViewer();
        var focus = await beeps.GetAsync(viewer, id, cancellationToken);
        var thread = focus.IsSuccess ? await beeps.GetThreadAsync(viewer, id, After, PageRequest.MaxLimit, cancellationToken) : null;
        if (thread is not { IsSuccess: true })
        {
            return NotFound();
        }

        (Focus, Thread) = (focus.Value, thread.Value);
        return Page();
    }
}
