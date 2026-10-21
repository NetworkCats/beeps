using Beeps.Features.Avatars;
using Beeps.Features.Media;
using Beeps.Features.Shared;

namespace Beeps.Features.Beeps;

/// <summary>
/// Turns stored rows into what a viewer sees: relations, avatar URLs, mentions, hashtags and live counts.
/// Shared by every feature that shows beeps, so they all look the same.
/// </summary>
public sealed partial class BeepViews(IBeepStore store, IBeepCounterStore counters, AvatarUrls avatars, MediaUrls media, ILogger<BeepViews> logger)
{
    private static readonly LogThrottle CounterFailures = new();

    public ActorView Actor(ActorRow actor, Viewer? viewer)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return new ActorView(actor.Id, actor.Kind, actor.Handle, actor.DisplayName, avatars.For(actor.Id, actor.AvatarKey), Relations.Of(viewer, actor.Id, actor.OwnerId));
    }

    /// <summary>Same order as <paramref name="rows"/>. One query each for mentions and images and one Valkey call for counts, however many rows.</summary>
    public async Task<IReadOnlyList<BeepView>> BuildAsync(IReadOnlyList<BeepRow> rows, Viewer? viewer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        var mentions = await store.GetMentionsAsync(ids, cancellationToken);
        var attached = await store.GetMediaAsync(ids, cancellationToken);
        var live = await LiveCountsAsync(ids, cancellationToken);
        return [.. rows.Select(row =>
        {
            var counts = live.TryGetValue(row.Id, out var current) ? current : new BeepCounts(row.LikeCount, row.ReplyCount);
            IReadOnlyList<ActorView> mentioned = mentions.TryGetValue(row.Id, out var actors) ? [.. actors.Select(a => Actor(a, viewer))] : [];
            IReadOnlyList<MediaView> images = attached.TryGetValue(row.Id, out var items) ? [.. items.Select(media.View)] : [];
            return new BeepView(
                row.Id,
                row.Text,
                Actor(row.Author, viewer),
                row.ReplyToId,
                row.RootId,
                row.CreatedAt,
                Math.Max(0, counts.Likes),
                Math.Max(0, counts.Replies),
                row.Liked,
                mentioned,
                BeepText.Hashtags(row.Text),
                images,
                viewer?.MayDeleteBeepOf(row.Author.Id, row.Author.OwnerId) ?? false,
                row.UnderReview);
        })];
    }

    public async Task<CursorPage<BeepView>> BuildPageAsync(CursorPage<BeepRow> page, Viewer? viewer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new CursorPage<BeepView>(await BuildAsync(page.Items, viewer, cancellationToken), page.NextCursor);
    }

    // Counts are decoration: with Valkey away, the written-back columns are a few seconds old at worst.
    private async Task<IReadOnlyDictionary<Guid, BeepCounts>> LiveCountsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        try
        {
            return await counters.GetAsync(ids, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (CounterFailures.Allow())
            {
                LogCountersUnavailable(logger, exception);
            }
            return new Dictionary<Guid, BeepCounts>();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live beep counts are unavailable; showing the written-back counts (logged at most once a minute)")]
    private static partial void LogCountersUnavailable(ILogger logger, Exception exception);
}
