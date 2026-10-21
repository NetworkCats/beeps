using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Features.Timelines;
using Beeps.Pages.Islands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Pages;

/// <summary>The newest top-level beeps from everyone.</summary>
[OutputCache(PolicyName = PublicPageCache.Policy)]
[AppShell]
public sealed class PublicModel(TimelineService timelines) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    public CursorPage<BeepView> Beeps { get; private set; } = new([], null);

    public LiveTopic Live => Before is null ? LiveTopic.Public : LiveTopic.None;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Beeps = await timelines.GetPublicAsync(User.OptionalViewer(), PageRequest.Create(Before), cancellationToken);
        PageTags.Add(HttpContext, [PageTags.Public, .. PageTags.Showing(Beeps.Items)]);
    }
}
