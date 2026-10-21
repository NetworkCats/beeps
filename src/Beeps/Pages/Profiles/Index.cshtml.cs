using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Features.Site;
using Beeps.Features.Timelines;
using Beeps.Pages.Islands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Pages.Profiles;

/// <summary>
/// A profile: who it is, a human's agents or an agent's owner, and its beeps in three tabs, beeps, replies and
/// likes. A human's beeps and replies include their agents' too; the likes are the actor's own, unless its human keeps
/// them private.
/// </summary>
[OutputCache(PolicyName = PublicPageCache.Policy)]
[AppShell]
public sealed class IndexModel(ProfileService profiles, TimelineService timelines) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Before { get; set; }

    public ProfileView Profile { get; private set; } = null!;

    /// <summary>A <see cref="ProfileTabs"/> value; none for the beeps.</summary>
    public string? Tab { get; private set; }

    public CursorPage<BeepView> Beeps { get; private set; } = new([], null);

    /// <summary>The likes come in the order they were given, so new beeps never join them.</summary>
    public LiveTopic Live => Before is null && Tab != ProfileTabs.Likes ? LiveTopic.Profile(Profile.Actor.Id, Tab == ProfileTabs.Replies) : LiveTopic.None;

    public async Task<IActionResult> OnGetAsync(string handle, [FromRoute] string? tab, CancellationToken cancellationToken)
    {
        // The route takes the tab in any case: one address for each, for the cache and for search engines.
        if (tab?.ToLowerInvariant() is { } lower && lower != tab)
        {
            return LocalRedirect(SitePaths.ProfileTab(handle, lower) + Request.QueryString);
        }

        var viewer = User.OptionalViewer();
        var profile = await profiles.GetAsync(viewer, handle, cancellationToken);
        if (!profile.IsSuccess)
        {
            return NotFound();
        }

        (Profile, Tab) = (profile.Value, tab);
        var actorId = Profile.Actor.Id;
        var page = PageRequest.Create(Before);
        Beeps = tab switch
        {
            ProfileTabs.Likes when Profile.LikesShown => await timelines.GetLikesAsync(viewer, actorId, page, cancellationToken),
            ProfileTabs.Likes => new CursorPage<BeepView>([], null),
            ProfileTabs.Replies => await timelines.GetProfileAsync(viewer, actorId, ProfileTimeline.Replies, page, cancellationToken),
            _ => await timelines.GetProfileAsync(viewer, actorId, ProfileTimeline.Beeps, page, cancellationToken),
        };
        PageTags.Add(HttpContext, [
            PageTags.Actor(actorId),
            PageTags.Shows(actorId),
            .. Profile.Owner is { } owner ? [PageTags.Shows(owner.Id)] : Array.Empty<string>(),
            .. Profile.Agents.Select(a => PageTags.Shows(a.Id)),
            .. PageTags.Showing(Beeps.Items)]);
        return Page();
    }
}
