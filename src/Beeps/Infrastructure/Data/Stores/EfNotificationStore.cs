using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Notifications;
using Beeps.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfNotificationStore(BeepsDbContext db) : INotificationStore
{
    /// <summary>
    /// How many of an entry's others are checked one by one for being visible. Past this (a beep with thousands of likes)
    /// they are counted off the index alone: the few from people hidden since make no difference there.
    /// </summary>
    private const int OthersChecked = 1000;

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
        // One statement for the whole batch; existing ones (a retried job) are skipped by the unique index. Each new one
        // supersedes the older ones like it, and is superseded itself when a newer one was added first (Entries).
        await db.Database.ExecuteSqlAsync(
            $"""
            WITH added AS (
                INSERT INTO notifications (id, recipient_id, type, from_actor_id, beep_id, created_at, superseded)
                SELECT t.*, EXISTS (
                    SELECT 1 FROM notifications AS o
                    WHERE o.recipient_id = t.recipient_id AND o.beep_id = t.beep_id AND o.type = t.type AND o.id > t.id)
                FROM unnest({ids}::uuid[], {recipients}::uuid[], {types}::text[], {from}::uuid[], {beeps}::uuid[], {created}::timestamptz[])
                    AS t(id, recipient_id, type, from_actor_id, beep_id, created_at)
                ON CONFLICT DO NOTHING
                RETURNING id, recipient_id, type, beep_id)
            UPDATE notifications AS o SET superseded = TRUE
            FROM added AS a
            WHERE o.recipient_id = a.recipient_id AND o.beep_id = a.beep_id AND o.type = a.type AND o.id < a.id AND NOT o.superseded
            """,
            cancellationToken);
    }

    public async Task<CursorPage<NotificationRow>> ListAsync(Guid recipientId, PageRequest page, bool unreadOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var alike = FromVisible(recipientId);
        var heads = Heads(recipientId);
        if (unreadOnly)
        {
            heads = heads.Where(h => h.ReadAt == null);
        }

        if (page.Before is { } before)
        {
            heads = heads.Where(h => h.Id < before);
        }

        // An entry is never newer than its head, so heads up to `since` have none newer either.
        if (page.SinceId is { } sinceHead)
        {
            heads = heads.Where(h => h.Id > sinceHead);
        }

        var entries = Entries(recipientId, heads);
        if (unreadOnly)
        {
            entries = entries.Where(e => e.Entry.ReadAt == null);
        }

        // Then by the entry's id, which is the one the caller was given: the head's is newer when it is from someone
        // now hidden, and the same entry would come back as new on every poll.
        if (page.SinceId is { } since)
        {
            entries = entries.Where(e => e.Entry.Id > since);
        }

        if (page.Until is { } until)
        {
            entries = entries.Where(e => e.Entry.Id < until);
        }

        var found = await entries
            .OrderByDescending(e => e.HeadId)
            .Take(page.Limit + 1)
            .Join(db.Set<Actor>(), e => e.Entry.FromActorId, a => a.Id, (e, a) => new { e.HeadId, n = e.Entry, a })
            .OrderByDescending(x => x.HeadId)
            .Select(x => new
            {
                x.HeadId,
                Row = new NotificationRow(
                    x.n.Id,
                    x.n.Type,
                    new ActorRow(x.a.Id, x.a.Kind, x.a.Handle, x.a.DisplayName, x.a.OwnerId, x.a.AvatarMedia == null || x.a.AvatarMedia.Status != MediaStatus.Ready ? null : x.a.AvatarMedia.StorageKey),
                    x.n.BeepId,
                    x.n.CreatedAt,
                    x.n.ReadAt,
                    alike.Where(m => m.BeepId == x.n.BeepId && m.Type == x.n.Type && m.Id < x.n.Id).Take(OthersChecked).Count()),
            })
            .ToListAsync(cancellationToken);
        // The cursor is the head's: an entry whose newest one is from someone now hidden sorts by that one all the same.
        var cursor = CursorPage.FromOverfetch(found, page.Limit, f => f.HeadId).NextCursor;
        var rows = found.Select(f => f.Row).ToList();
        var many = rows.Where(r => r.Others == OthersChecked).Select(r => r.Id).ToArray();
        if (many.Length > 0)
        {
            var all = db.Set<Notification>().Where(m => m.RecipientId == recipientId);
            var counted = await all
                .Where(e => many.Contains(e.Id))
                .Select(e => new { e.Id, Others = all.Count(m => m.BeepId == e.BeepId && m.Type == e.Type && m.Id < e.Id) })
                .ToDictionaryAsync(e => e.Id, e => e.Others, cancellationToken);
            rows = [.. rows.Select(r => counted.TryGetValue(r.Id, out var others) ? r with { Others = others } : r)];
        }

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

        return new CursorPage<NotificationRow>([.. rows.Take(page.Limit)], cursor);
    }

    /// <remarks>
    /// Only unread heads are walked (<c>ix_notifications_unread_heads</c>): an entry's notifications are only ever marked
    /// read up to an id or by their beep, so when its newest visible one is unread, so is its head.
    /// </remarks>
    public Task<int> CountUnreadAsync(Guid recipientId, int? max, CancellationToken cancellationToken)
    {
        var unread = Entries(recipientId, Heads(recipientId).Where(h => h.ReadAt == null)).Where(e => e.Entry.ReadAt == null);
        // Any of them will do for a capped count: in order, every unread head was read and sorted first.
        return max is { } limit
            ? unread.Take(limit).CountAsync(cancellationToken)
            : unread.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListUnreadRecipientsAsync(IReadOnlyCollection<Guid> beepIds, IReadOnlyCollection<Guid> fromActorIds, int max, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beepIds);
        ArgumentNullException.ThrowIfNull(fromActorIds);
        var (beeps, from) = (beepIds.ToArray(), fromActorIds.ToArray());
        var unread = db.Set<Notification>().Where(n => n.ReadAt == null);
        // Two index-only scans (the beep_id and from_actor_id indexes carry the recipient and read_at): as one OR, the
        // rows themselves were read, one per like of a popular beep.
        return await unread.Where(n => beeps.Contains(n.BeepId)).Select(n => n.RecipientId)
            .Union(unread.Where(n => from.Contains(n.FromActorId)).Select(n => n.RecipientId))
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

    /// <summary>The newest notification of each type about each beep (likes of one beep): one per inbox entry.</summary>
    /// <remarks>
    /// <see cref="Notification.Superseded"/> narrows the walk to these; the check for a newer one covers those added side
    /// by side (two likes at once), which cannot see each other to set it.
    /// </remarks>
    private IQueryable<Notification> Heads(Guid recipientId)
    {
        var all = db.Set<Notification>().Where(m => m.RecipientId == recipientId);
        return all
            .AsNoTracking()
            .Where(h => !h.Superseded && !all.Any(m => m.BeepId == h.BeepId && m.Type == h.Type && m.Id > h.Id));
    }

    /// <summary>
    /// The inbox entries of <paramref name="heads"/> the recipient can still see: of each head's notifications (likes of one
    /// beep), the newest from a visible actor, which stands for the rest (<c>ix_notifications_beep</c>, usually the head).
    /// </summary>
    private IQueryable<InboxEntry> Entries(Guid recipientId, IQueryable<Notification> heads)
    {
        var alike = FromVisible(recipientId);
        var visibleBeeps = BeepQueries.Visible(db, recipientId);
        return heads
            .Where(h => visibleBeeps.Any(b => b.Id == h.BeepId))
            .SelectMany(
                h => alike.Where(m => m.BeepId == h.BeepId && m.Type == h.Type && m.Id <= h.Id).OrderByDescending(m => m.Id).Take(1),
                (h, m) => new InboxEntry { HeadId = h.Id, Entry = m });
    }

    /// <summary>The recipient's notifications from actors that are still visible and not blocked, whatever they are about.</summary>
    private IQueryable<Notification> FromVisible(Guid recipientId)
    {
        var visibleActors = BeepQueries.VisibleActors(db, recipientId);
        return db.Set<Notification>()
            .AsNoTracking()
            .Where(n => n.RecipientId == recipientId && visibleActors.Any(a => a.Id == n.FromActorId));
    }

    /// <remarks>Set by initializer, so EF can keep composing queries on its members.</remarks>
    private sealed class InboxEntry
    {
        /// <summary>What the entry sorts by: its head's id.</summary>
        public Guid HeadId { get; init; }

        public required Notification Entry { get; init; }
    }
}
