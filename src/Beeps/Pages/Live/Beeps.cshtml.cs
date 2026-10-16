using Beeps.Features.Auth;
using Beeps.Features.Live;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Live;

/// <summary>What a live page fetches when the stream announces new beeps: just the cards, no layout.</summary>
public sealed class BeepsModel(LiveFeedService live) : PageModel
{
    /// <summary>The page's <see cref="LiveTopic"/>.</summary>
    [BindProperty(SupportsGet = true)] public string? Watch { get; set; }

    /// <summary>The newest beep a list shows, or the last one of a thread; the empty id when the page shows none.</summary>
    [BindProperty(SupportsGet = true)] public Guid? After { get; set; }

    /// <summary>The page the cards go on, where their like and delete forms return to.</summary>
    [BindProperty(SupportsGet = true, Name = "return")] public string? Return { get; set; }

    public LiveBeeps Beeps { get; private set; } = new([], false, new Dictionary<Guid, string?>());

    public string ReturnUrl => Url.IsLocalUrl(Return) ? Return! : "/";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await live.GetNewBeepsAsync(User.Viewer(), Watch, After ?? Guid.Empty, cancellationToken);
        if (!result.IsSuccess)
        {
            return BadRequest();
        }

        Beeps = result.Value;
        return Page();
    }
}
