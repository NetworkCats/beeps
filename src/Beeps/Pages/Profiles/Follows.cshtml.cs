using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Features.Site;
using Beeps.Pages.Islands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Pages.Profiles;

/// <summary>
/// Whom a human follows, or who follows them, most recently followed first, unless they keep the lists to themselves.
/// Agents neither follow nor are followed, so they have no such page.
/// </summary>
[OutputCache(PolicyName = PublicPageCache.Policy)]
[AppShell]
public sealed class FollowsModel(ProfileService profiles) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    public ProfileView Profile { get; private set; } = null!;

    public FollowList List { get; private set; }

    public CursorPage<PersonView> People { get; private set; } = new([], null);

    public async Task<IActionResult> OnGetAsync(string handle, string list, CancellationToken cancellationToken)
    {
        // The route takes the list in any case: one address for each, for the cache and for search engines.
        var lower = list.ToLowerInvariant();
        if (lower != list)
        {
            return LocalRedirect(SitePaths.ProfileTab(handle, lower) + Request.QueryString);
        }

        var viewer = User.OptionalViewer();
        var profile = await profiles.GetAsync(viewer, handle, cancellationToken);
        if (!profile.IsSuccess || profile.Value.Actor.Kind != ActorKind.Human)
        {
            return NotFound();
        }

        (Profile, List) = (profile.Value, list == ProfileTabs.Followers ? FollowList.Followers : FollowList.Following);
        if (Profile.FollowsShown)
        {
            People = await profiles.GetFollowsAsync(viewer, Profile.Actor.Id, List, PageRequest.Create(Before), cancellationToken);
        }

        PageTags.Add(HttpContext, [PageTags.Shows(Profile.Actor.Id), .. People.Items.Select(p => PageTags.Shows(p.Actor.Id))]);
        return Page();
    }
}
