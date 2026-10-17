using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Features.Timelines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Pages.Tags;

/// <summary>Every beep with a hashtag, newest first (PLAN 6.19).</summary>
[OutputCache(PolicyName = PublicPageCache.Policy)]
public sealed class IndexModel(TimelineService timelines) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    /// <summary>As written in the URL; the lookup itself ignores case and width.</summary>
    public string Tag { get; private set; } = "";

    public CursorPage<BeepView> Beeps { get; private set; } = new([], null);

    public LiveTopic Live { get; private set; } = LiveTopic.None;

    public async Task<IActionResult> OnGetAsync(string tag, CancellationToken cancellationToken)
    {
        var result = await timelines.GetHashtagAsync(User.OptionalViewer(), tag, PageRequest.Create(Before), cancellationToken);
        if (!result.IsSuccess)
        {
            return NotFound();
        }

        (Tag, Beeps) = (tag.TrimStart('#'), result.Value);
        if (BeepText.TryNormalizeHashtag(tag, out var normalized))
        {
            PageTags.Add(HttpContext, [PageTags.Hashtag(normalized), .. PageTags.Showing(Beeps.Items)]);
            Live = Before is null ? LiveTopic.ForTag(normalized) : LiveTopic.None;
        }

        return Page();
    }
}
