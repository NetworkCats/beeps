using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Live;

/// <param name="InThread">The beeps belong to the thread the page shows (oldest first).</param>
/// <param name="ReplyingTo">For replies in a thread: the handle of the author each one answers, when that beep is visible.</param>
public sealed record LiveBeeps(IReadOnlyList<BeepView> Items, bool InThread, IReadOnlyDictionary<Guid, string?> ReplyingTo);

/// <summary>
/// What a live page adds when the stream announces something new (PLAN 6.10): the same queries that rendered
/// the page, asked only for what came after the beep it shows last. So a new beep appears exactly when a reload
/// would have shown it, for this viewer.
/// </summary>
public sealed class LiveFeedService(TimelineService timelines, BeepService beeps, IStringLocalizer<SharedResource> localizer)
{
    /// <summary>More than this many new beeps at once is unlikely between two fetches a second apart; the oldest of them are left out.</summary>
    public const int Limit = 50;

    /// <param name="cursor">For a newest-first list: the newest beep the page shows; for a thread: the last one.</param>
    public async Task<Result<LiveBeeps>> GetNewBeepsAsync(Viewer viewer, string? watch, Guid cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (!LiveTopic.TryParse(watch, out var topic) || !(topic.IsBeepList || topic.Kind == LiveTopicKind.Thread))
        {
            return Error.Validation(LiveErrorCodes.InvalidWatch, localizer["Error.LiveWatchInvalid"]);
        }

        if (topic.Kind == LiveTopicKind.Thread)
        {
            var replies = await beeps.ListThreadAsync(viewer, topic.Id, cursor, Limit, cancellationToken);
            var parents = replies.Items.Where(b => b.ReplyToId is not null).Select(b => b.ReplyToId!.Value).ToHashSet();
            return new LiveBeeps(replies.Items, InThread: true, await beeps.GetAuthorHandlesAsync(viewer, parents, cancellationToken));
        }

        var since = PageRequest.Create(sinceId: cursor, limit: Limit);
        var page = topic.Kind switch
        {
            LiveTopicKind.Home => await timelines.GetHomeAsync(viewer, since, cancellationToken),
            LiveTopicKind.Public => await timelines.GetPublicAsync(viewer, since, cancellationToken),
            LiveTopicKind.Profile => await timelines.GetProfileAsync(viewer, topic.Id, ProfileTimeline.Beeps, since, cancellationToken),
            LiveTopicKind.ProfileReplies => await timelines.GetProfileAsync(viewer, topic.Id, ProfileTimeline.Replies, since, cancellationToken),
            _ => (await timelines.GetHashtagAsync(viewer, topic.Tag, since, cancellationToken)).Value,
        };
        return new LiveBeeps(page.Items, InThread: false, new Dictionary<Guid, string?>());
    }
}
