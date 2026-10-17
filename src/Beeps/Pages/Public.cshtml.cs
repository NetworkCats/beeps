using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Features.Timelines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Pages;

/// <summary>The newest top-level beeps from everyone (PLAN 1).</summary>
[OutputCache(PolicyName = PublicPageCache.Policy)]
public sealed class PublicModel(TimelineService timelines) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    public CursorPage<BeepView> Beeps { get; private set; } = new([], null);

    public LiveTopic Live => Before is null ? LiveTopic.Public : LiveTopic.None;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        PageTags.Add(HttpContext, PageTags.Public);
        Beeps = await timelines.GetPublicAsync(User.OptionalViewer(), PageRequest.Create(Before), cancellationToken);
    }
}
