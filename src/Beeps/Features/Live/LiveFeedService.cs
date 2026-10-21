using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Live;

/// <summary>
/// What a live page adds when the stream announces something new: the same queries that rendered
/// the page, asked only for what came after the beep it shows last. So a new beep appears exactly when a reload
/// would have shown it, for this viewer.
/// </summary>
public sealed class LiveFeedService(TimelineService timelines, BeepService beeps, IStringLocalizer<SharedResource> localizer)
{
    /// <summary>More than this many new beeps at once is unlikely between two fetches a second apart; the oldest of them are left out.</summary>
    public const int Limit = 50;

    /// <param name="cursor">For a newest-first list: the newest beep the page shows; for a thread: the last one.</param>
    /// <param name="before">For a newest-first list: only beeps older than this one. More than <see cref="Limit"/> came at
    /// once: the page asks for the newest first, then for the rest below them, so none is left out.</param>
    public async Task<Result<IReadOnlyList<BeepView>>> GetNewBeepsAsync(Viewer viewer, string? watch, Guid cursor, Guid? before, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (!LiveTopic.TryParse(watch, out var topic) || !(topic.IsBeepList || topic.Kind == LiveTopicKind.Thread))
        {
            return Error.Validation(LiveErrorCodes.InvalidWatch, localizer["Error.LiveWatchInvalid"]);
        }

        if (topic.Kind == LiveTopicKind.Thread)
        {
            return Result.Of((await beeps.ListThreadAsync(viewer, topic.Id, cursor, Limit, cancellationToken)).Items);
        }

        var since = PageRequest.Create(before: before, sinceId: cursor, limit: Limit);
        var page = topic.Kind switch
        {
            LiveTopicKind.Home => await timelines.GetHomeAsync(viewer, since, cancellationToken),
            LiveTopicKind.Public => await timelines.GetPublicAsync(viewer, since, cancellationToken),
            LiveTopicKind.Profile => await timelines.GetProfileAsync(viewer, topic.Id, ProfileTimeline.Beeps, since, cancellationToken),
            LiveTopicKind.ProfileReplies => await timelines.GetProfileAsync(viewer, topic.Id, ProfileTimeline.Replies, since, cancellationToken),
            _ => (await timelines.GetHashtagAsync(viewer, topic.Tag, since, cancellationToken)).Value,
        };
        return Result.Of(page.Items);
    }

    /// <summary>
    /// Beeps a page shows, again, when they changed (images ready, under review). The ones the viewer may no longer see
    /// are left out, so the page drops them, as a reload would.
    /// </summary>
    public async Task<Result<IReadOnlyList<BeepView>>> GetBeepsAsync(Viewer viewer, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count > LiveService.MaxWatchedBeeps)
        {
            return Error.Validation(LiveErrorCodes.InvalidBeeps, localizer["Error.LiveBeepsInvalid", LiveService.MaxWatchedBeeps]);
        }

        return Result.Of(await beeps.GetManyAsync(viewer, ids, cancellationToken));
    }
}
