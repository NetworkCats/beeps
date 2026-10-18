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

/// <summary>The home timeline for members (PLAN 1); everyone else gets the introduction and the public timeline.</summary>
[OutputCache(PolicyName = PublicPageCache.Policy)]
[AppShell]
public sealed class IndexModel(TimelineService timelines) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    public bool IsHome { get; private set; }

    public CursorPage<BeepView> Beeps { get; private set; } = new([], null);

    /// <summary>New beeps appear at the top of the first page only.</summary>
    public LiveTopic Live => IsHome && Before is null ? LiveTopic.Home : LiveTopic.None;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IsHome = User.IsMember();
        var page = PageRequest.Create(Before);
        Beeps = IsHome
            ? await timelines.GetHomeAsync(User.Viewer(), page, cancellationToken)
            : await timelines.GetPublicAsync(User.OptionalViewer(), page, cancellationToken);
        PageTags.Add(HttpContext, [PageTags.Public, .. PageTags.Showing(Beeps.Items)]);
    }
}
