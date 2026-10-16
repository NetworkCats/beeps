using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Features.Timelines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Pages.Profiles;

/// <summary>
/// A profile (PLAN 1): who it is, a human's agents or an agent's owner, and its beeps in two tabs, beeps and
/// replies. A human's tabs include their agents' beeps too.
/// </summary>
[OutputCache(PolicyName = PublicPageCache.Policy)]
public sealed class IndexModel(ProfileService profiles, TimelineService timelines) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    public ProfileView Profile { get; private set; } = null!;

    public ProfileTimeline Tab { get; private set; }

    public CursorPage<BeepView> Beeps { get; private set; } = new([], null);

    public LiveTopic Live => Before is null ? LiveTopic.Profile(Profile.Actor.Id, Tab == ProfileTimeline.Replies) : LiveTopic.None;

    public async Task<IActionResult> OnGetAsync(string handle, string? tab, CancellationToken cancellationToken)
    {
        var viewer = User.OptionalViewer();
        var profile = await profiles.GetAsync(viewer, handle, cancellationToken);
        if (!profile.IsSuccess)
        {
            return NotFound();
        }

        (Profile, Tab) = (profile.Value, tab is null ? ProfileTimeline.Beeps : ProfileTimeline.Replies);
        Beeps = await timelines.GetProfileAsync(viewer, Profile.Actor.Id, Tab, PageRequest.Create(Before), cancellationToken);
        return Page();
    }
}
