using Beeps.Features.Beeps;
using Beeps.Features.Shared;

namespace Beeps.Features.Timelines;

/// <summary>Read-time timelines (PLAN 6.6, first stage): each is one indexed query over the beeps table.</summary>
public interface ITimelineStore
{
    /// <summary>Top-level beeps by anyone.</summary>
    Task<CursorPage<BeepRow>> PublicAsync(PageRequest page, Guid? viewerId, CancellationToken cancellationToken);

    /// <summary>
    /// Beeps by the human, the humans they follow, and every agent of any of them. Replies are included
    /// only when the beep they answer is by one of those same authors, so the timeline shows conversations
    /// among them but not their replies to strangers.
    /// </summary>
    Task<CursorPage<BeepRow>> HomeAsync(Guid humanId, PageRequest page, Guid? viewerId, CancellationToken cancellationToken);

    /// <summary>Beeps and replies by the actor and, for a human, by all of their agents.</summary>
    Task<CursorPage<BeepRow>> ProfileAsync(Guid actorId, PageRequest page, Guid? viewerId, CancellationToken cancellationToken);

    /// <summary>Beeps and replies tagged with the normalized hashtag.</summary>
    Task<CursorPage<BeepRow>> HashtagAsync(string normalizedTag, PageRequest page, Guid? viewerId, CancellationToken cancellationToken);
}
