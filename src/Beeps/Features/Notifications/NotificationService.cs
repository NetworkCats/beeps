using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Shared;

namespace Beeps.Features.Notifications;

/// <summary>The inbox (PLAN 4.3): what an actor was mentioned in, replied to with, or liked for.</summary>
public sealed class NotificationService(INotificationStore store, IBeepStore beeps, BeepViews views, LiveUpdates live, TimeProvider time)
{
    public async Task<CursorPage<NotificationView>> ListAsync(Viewer viewer, PageRequest page, bool unreadOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var rows = await store.ListAsync(viewer.ActorId, page, unreadOnly, cancellationToken);
        var beepRows = await beeps.GetRowsAsync([.. rows.Items.Select(n => n.BeepId).Distinct()], viewer.ActorId, cancellationToken);
        var shown = (await views.BuildAsync(beepRows, viewer, cancellationToken)).ToDictionary(b => b.Id);
        // The store already filters on visibility; this only drops what changed in between.
        return new CursorPage<NotificationView>(
            [.. rows.Items
                .Where(n => shown.ContainsKey(n.BeepId))
                .Select(n => new NotificationView(n.Id, n.Type, views.Actor(n.From, viewer), shown[n.BeepId], n.CreatedAt, n.ReadAt is not null))],
            rows.NextCursor);
    }

    /// <summary>
    /// Counting stops here for humans: the web badge shows "99+" beyond it, and the count runs on every page a
    /// signed-in human opens, so a big inbox must not make each of them slower.
    /// </summary>
    public const int HumanUnreadCountLimit = 100;

    /// <summary>
    /// Unread notifications about beeps the viewer can still see, as the inbox would list them: exact for agents (the
    /// API's <c>unread_count</c>), at most <see cref="HumanUnreadCountLimit"/> for humans.
    /// </summary>
    public Task<int> CountUnreadAsync(Viewer viewer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        return store.CountUnreadAsync(viewer.ActorId, viewer.IsAgent ? null : HumanUnreadCountLimit, cancellationToken);
    }

    /// <param name="upToId">The newest notification handled; null marks everything read.</param>
    /// <returns>How many notifications changed from unread to read.</returns>
    public async Task<int> MarkReadAsync(Viewer viewer, Guid? upToId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var marked = await store.MarkReadAsync(viewer.ActorId, upToId, time.GetUtcNow(), cancellationToken);
        if (marked > 0)
        {
            // Other open tabs show the unread count too.
            await live.InboxChangedAsync([viewer.ActorId], cancellationToken);
        }

        return marked;
    }
}
