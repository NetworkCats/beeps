using Beeps.Features.Auth;
using Beeps.Features.Live;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Beeps.Pages.Live;

/// <summary>
/// What a live page fetches: just the cards, no layout. Either the beeps after the one it shows last (the stream
/// announced new ones, or the page catches up), or, with <see cref="Ids"/>, some it shows, drawn again.
/// </summary>
public sealed class BeepsModel(LiveFeedService live) : PageModel
{
    /// <summary>The page's <see cref="LiveTopic"/>.</summary>
    [BindProperty(SupportsGet = true)] public string? Watch { get; set; }

    /// <summary>The newest beep a list shows, or the last one of a thread; the empty id when the page shows none.</summary>
    [BindProperty(SupportsGet = true)] public Guid? After { get; set; }

    /// <summary>For a list: only beeps older than this one (the rest of a burst too big for one answer).</summary>
    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    /// <summary>Comma-separated: the beeps to draw again, instead of the new ones.</summary>
    [BindProperty(SupportsGet = true)] public string? Ids { get; set; }

    /// <summary>The beep a thread page is about, which is drawn marked.</summary>
    [BindProperty(SupportsGet = true)] public Guid? Focus { get; set; }

    /// <summary>The page the cards go on, where their like and delete forms return to.</summary>
    [BindProperty(SupportsGet = true, Name = "return")] public string? Return { get; set; }

    public LiveBeeps Beeps { get; private set; } = new([], false, new Dictionary<Guid, string?>());

    public string ReturnUrl => Url.IsLocalUrl(Return) ? Return! : "/";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Result<LiveBeeps> result;
        if (Ids is null)
        {
            result = await live.GetNewBeepsAsync(User.Viewer(), Watch, After ?? Guid.Empty, Before, cancellationToken);
        }
        else if (TryParseIds(Ids, out var ids))
        {
            result = await live.GetBeepsAsync(User.Viewer(), Watch, ids, cancellationToken);
        }
        else
        {
            return BadRequest();
        }

        if (!result.IsSuccess)
        {
            return BadRequest();
        }

        Beeps = result.Value;
        return Page();
    }

    private static bool TryParseIds(string value, out List<Guid> ids)
    {
        ids = [];
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var id))
            {
                return false;
            }

            ids.Add(id);
        }

        return true;
    }
}
