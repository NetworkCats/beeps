using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Notifications;
using Beeps.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfNotificationStore(BeepsDbContext db) : INotificationStore
{
    public async Task<BeepAudience?> GetBeepAudienceAsync(Guid beepId, CancellationToken cancellationToken)
    {
        var beep = await db.Set<Beep>()
            .AsNoTracking()
            .Where(b => b.Id == beepId && b.DeletedAt == null)
            .Select(b => new
            {
                b.AuthorId,
                ReplyToAuthorId = db.Set<Beep>().Where(p => p.Id == b.ReplyToId).Select(p => (Guid?)p.AuthorId).FirstOrDefault(),
                Mentioned = db.Set<Mention>().Where(m => m.BeepId == b.Id).Select(m => m.ActorId).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        return beep is null ? null : new BeepAudience(beep.AuthorId, beep.ReplyToAuthorId, beep.Mentioned);
    }

    public Task<Guid?> GetLikedBeepAuthorAsync(Guid beepId, Guid actorId, CancellationToken cancellationToken) =>
        db.Set<Like>()
            .Where(l => l.BeepId == beepId && l.ActorId == actorId)
            .Join(db.Set<Beep>().Where(b => b.DeletedAt == null), l => l.BeepId, b => b.Id, (l, b) => (Guid?)b.AuthorId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        var ids = notifications.Select(n => n.Id).ToArray();
        var recipients = notifications.Select(n => n.RecipientId).ToArray();
        var types = notifications.Select(n => EnumText.ToValue(n.Type)).ToArray();
        var from = notifications.Select(n => n.FromActorId).ToArray();
        var beeps = notifications.Select(n => n.BeepId).ToArray();
        var created = notifications.Select(n => n.CreatedAt).ToArray();
        // One statement for the whole batch; existing ones (a retried job) are skipped by the unique index.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO notifications (id, recipient_id, type, from_actor_id, beep_id, created_at)
            SELECT * FROM unnest({ids}::uuid[], {recipients}::uuid[], {types}::text[], {from}::uuid[], {beeps}::uuid[], {created}::timestamptz[])
            ON CONFLICT DO NOTHING
            """,
            cancellationToken);
    }

    public async Task<CursorPage<NotificationRow>> ListAsync(Guid recipientId, PageRequest page, bool unreadOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var alike = FromVisible(recipientId);
        var entries = Entries(recipientId);
        if (unreadOnly)
        {
            entries = entries.Where(n => n.ReadAt == null);
        }

        if (page.Before is { } before)
        {
            entries = entries.Where(n => n.Id < before);
        }

        if (page.SinceId is { } since)
        {
            entries = entries.Where(n => n.Id > since);
        }

        var rows = await entries
            .OrderByDescending(n => n.Id)
            .Take(page.Limit + 1)
            .Join(db.Set<Actor>(), n => n.FromActorId, a => a.Id, (n, a) => new { n, a })
            .OrderByDescending(x => x.n.Id)
            .Select(x => new NotificationRow(
                x.n.Id,
                x.n.Type,
                new ActorRow(x.a.Id, x.a.Kind, x.a.Handle, x.a.DisplayName, x.a.OwnerId, x.a.AvatarMedia == null || x.a.AvatarMedia.Status != MediaStatus.Ready ? null : x.a.AvatarMedia.StorageKey),
                x.n.BeepId,
                x.n.CreatedAt,
                x.n.ReadAt,
                alike.Count(m => m.BeepId == x.n.BeepId && m.Type == x.n.Type && m.Id < x.n.Id)))
            .ToListAsync(cancellationToken);
        var merged = rows.Where(r => r.Others > 0).Select(r => r.Id).ToArray();
        if (merged.Length > 0)
        {
            // The newest few of each merged entry's others, in one statement (LATERAL).
            var earlier = await db.Set<Notification>()
                .Where(e => merged.Contains(e.Id))
                .SelectMany(
                    e => alike.Where(m => m.BeepId == e.BeepId && m.Type == e.Type && m.Id < e.Id).OrderByDescending(m => m.Id).Take(NotificationRow.EarlierShown),
                    (e, m) => new { Entry = e.Id, m.Id, m.FromActorId })
                .Join(db.Set<Actor>(), x => x.FromActorId, a => a.Id, (x, a) => new
                {
                    x.Entry,
                    x.Id,
                    Actor = new ActorRow(a.Id, a.Kind, a.Handle, a.DisplayName, a.OwnerId, a.AvatarMedia == null || a.AvatarMedia.Status != MediaStatus.Ready ? null : a.AvatarMedia.StorageKey),
                })
                .ToListAsync(cancellationToken);
            var byEntry = earlier.GroupBy(x => x.Entry).ToDictionary(g => g.Key, g => (IReadOnlyList<ActorRow>)[.. g.OrderByDescending(x => x.Id).Select(x => x.Actor)]);
            rows = [.. rows.Select(r => byEntry.TryGetValue(r.Id, out var actors) ? r with { Earlier = actors } : r)];
        }

        return CursorPage.FromOverfetch(rows, page.Limit, r => r.Id);
    }

    /// <remarks>With a limit, only the newest unread are looked at (<c>ix_notifications_unread</c>), however many there are.</remarks>
    public Task<int> CountUnreadAsync(Guid recipientId, int? max, CancellationToken cancellationToken)
    {
        var unread = Entries(recipientId).Where(n => n.ReadAt == null);
        return max is { } limit
            ? unread.OrderByDescending(n => n.Id).Take(limit).CountAsync(cancellationToken)
            : unread.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListUnreadRecipientsAsync(IReadOnlyCollection<Guid> beepIds, IReadOnlyCollection<Guid> fromActorIds, int max, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beepIds);
        ArgumentNullException.ThrowIfNull(fromActorIds);
        var (beeps, from) = (beepIds.ToArray(), fromActorIds.ToArray());
        return await db.Set<Notification>()
            .Where(n => n.ReadAt == null && (beeps.Contains(n.BeepId) || from.Contains(n.FromActorId)))
            .Select(n => n.RecipientId)
            .Distinct()
            .Take(max)
            .ToListAsync(cancellationToken);
    }

    public Task<int> MarkReadAsync(Guid recipientId, Guid? upToId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var unread = db.Set<Notification>().Where(n => n.RecipientId == recipientId && n.ReadAt == null);
        if (upToId is { } upTo)
        {
            unread = unread.Where(n => n.Id <= upTo);
        }

        return unread.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);
    }

    public Task<int> MarkSeenAsync(Guid recipientId, IReadOnlyCollection<Guid> seenBeepIds, Guid? openedBeepId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seenBeepIds);
        if (seenBeepIds.Count == 0 && openedBeepId is null)
        {
            return Task.FromResult(0);
        }

        // A like is read on the liked beep's own page only: seeing the beep elsewhere does not say who liked it.
        var seen = seenBeepIds.ToArray();
        return db.Set<Notification>()
            .Where(n => n.RecipientId == recipientId
                && n.ReadAt == null
                && (n.BeepId == openedBeepId || (n.Type != NotificationType.Like && seen.Contains(n.BeepId))))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);
    }

    /// <summary>
    /// The recipient's inbox entries: of each type's notifications about each beep they can still see (likes of one
    /// beep), the newest from a visible actor, which stands for the rest (<c>ix_notifications_beep</c>).
    /// </summary>
    private IQueryable<Notification> Entries(Guid recipientId)
    {
        // About the same beep, so as visible as the entry.
        var alike = FromVisible(recipientId);
        return Inbox(recipientId).Where(n => !alike.Any(m => m.BeepId == n.BeepId && m.Type == n.Type && m.Id > n.Id));
    }

    /// <summary>The recipient's notifications about beeps they can still see, from actors that are still visible and not blocked.</summary>
    private IQueryable<Notification> Inbox(Guid recipientId)
    {
        var visibleBeeps = BeepQueries.Visible(db, recipientId);
        return FromVisible(recipientId).Where(n => visibleBeeps.Any(b => b.Id == n.BeepId));
    }

    /// <summary>The recipient's notifications from actors that are still visible and not blocked, whatever they are about.</summary>
    private IQueryable<Notification> FromVisible(Guid recipientId)
    {
        var visibleActors = BeepQueries.VisibleActors(db, recipientId);
        return db.Set<Notification>()
            .AsNoTracking()
            .Where(n => n.RecipientId == recipientId && visibleActors.Any(a => a.Id == n.FromActorId));
    }
}
