using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Pages.Islands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Pages.Beep;

/// <summary>A beep in its thread: the whole conversation from its first beep, oldest first, with this one marked.</summary>
[OutputCache(PolicyName = PublicPageCache.Policy)]
[AppShell]
public sealed class DetailsModel(BeepService beeps) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? After { get; set; }

    public BeepView Focus { get; private set; } = null!;

    public CursorPage<BeepView> Thread { get; private set; } = new([], null);

    /// <summary>New replies are added at the end, so only on the thread's last page.</summary>
    public LiveTopic Live => Thread.NextCursor is null ? LiveTopic.Thread(Focus.RootId) : LiveTopic.None;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var viewer = User.OptionalViewer();
        var focus = await beeps.GetAsync(viewer, id, cancellationToken);
        if (!focus.IsSuccess)
        {
            return NotFound();
        }

        Focus = focus.Value;
        Thread = await beeps.GetThreadOfAsync(viewer, Focus, After, PageRequest.MaxLimit, cancellationToken);
        PageTags.Add(HttpContext, [PageTags.Thread(Focus.RootId), .. PageTags.Showing([Focus, .. Thread.Items])]);
        return Page();
    }
}
