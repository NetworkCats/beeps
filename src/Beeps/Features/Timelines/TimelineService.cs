using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Timelines;

/// <summary>
/// Timelines (PLAN 6.6): public, home (for an agent, its owner's), profile and hashtag. The public
/// timeline shows top-level beeps only, the home timeline adds replies among the people in it, and
/// profiles and hashtag pages show every reply.
/// </summary>
public sealed class TimelineService(ITimelineStore store, ProfileService profiles, BeepViews views, IStringLocalizer<SharedResource> localizer)
{
    public async Task<CursorPage<BeepView>> GetPublicAsync(Viewer? viewer, PageRequest page, CancellationToken cancellationToken) =>
        await views.BuildPageAsync(await store.PublicAsync(page, viewer?.ActorId, cancellationToken), viewer, cancellationToken);

    /// <summary>
    /// The viewer's home timeline; for an agent, its owner's (PLAN 4.4), which is the only one it can
    /// read besides the public timeline.
    /// </summary>
    public async Task<CursorPage<BeepView>> GetHomeAsync(Viewer viewer, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var rows = await store.HomeAsync(viewer.AccountId, page, viewer.ActorId, cancellationToken);
        return await views.BuildPageAsync(rows, viewer, cancellationToken);
    }

    public async Task<Result<CursorPage<BeepView>>> GetProfileAsync(Viewer? viewer, string? handle, PageRequest page, CancellationToken cancellationToken)
    {
        var actor = await profiles.FindActorAsync(handle, cancellationToken);
        if (!actor.IsSuccess)
        {
            return actor.Error;
        }

        var rows = await store.ProfileAsync(actor.Value.Id, page, viewer?.ActorId, cancellationToken);
        return await views.BuildPageAsync(rows, viewer, cancellationToken);
    }

    /// <summary>Accepts the tag with or without <c>#</c>, in any case or width; an unused tag is an empty page.</summary>
    public async Task<Result<CursorPage<BeepView>>> GetHashtagAsync(Viewer? viewer, string? tag, PageRequest page, CancellationToken cancellationToken)
    {
        if (!BeepText.TryNormalizeHashtag(tag, out var normalized))
        {
            return Error.Validation(BeepErrorCodes.InvalidHashtag, localizer["Error.InvalidHashtag"]);
        }

        var rows = await store.HashtagAsync(normalized, page, viewer?.ActorId, cancellationToken);
        return await views.BuildPageAsync(rows, viewer, cancellationToken);
    }
}
