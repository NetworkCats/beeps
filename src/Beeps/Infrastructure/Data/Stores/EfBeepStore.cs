using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfBeepStore(BeepsDbContext db) : IBeepStore
{
    public Task<BeepRef?> FindVisibleAsync(Guid beepId, Guid? viewerId, CancellationToken cancellationToken) =>
        BeepQueries.Visible(db, viewerId)
            .Where(b => b.Id == beepId)
            .Select(b => new BeepRef(b.Id, b.AuthorId, b.Author.OwnerId, b.RootId, b.ReplyToId, b.LikeCount, b.ReplyCount))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BeepRow>> GetRowsAsync(IReadOnlyCollection<Guid> beepIds, Guid? viewerId, CancellationToken cancellationToken) =>
        beepIds.Count == 0
            ? []
            : await BeepQueries.Visible(db, viewerId).Where(b => beepIds.Contains(b.Id)).Rows(db, viewerId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ActorRow>>> GetMentionsAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken)
    {
        if (beepIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ActorRow>>();
        }

        var rows = await db.Set<Mention>()
            .AsNoTracking()
            .Where(m => beepIds.Contains(m.BeepId))
            .Join(BeepQueries.VisibleActors(db), m => m.ActorId, a => a.Id, (m, a) => new
            {
                m.BeepId,
                Actor = new ActorRow(a.Id, a.Kind, a.Handle, a.DisplayName, a.OwnerId),
            })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.BeepId).ToDictionary(g => g.Key, g => (IReadOnlyList<ActorRow>)[.. g.Select(r => r.Actor)]);
    }

    public async Task<CursorPage<BeepRow>> ListThreadAsync(Guid rootId, Guid? after, int limit, Guid? viewerId, CancellationToken cancellationToken)
    {
        var thread = BeepQueries.Visible(db, viewerId).Where(b => b.RootId == rootId);
        if (after is { } cursor)
        {
            thread = thread.Where(b => b.Id > cursor);
        }

        var rows = await thread.OrderBy(b => b.Id).Take(limit + 1).Rows(db, viewerId).ToListAsync(cancellationToken);
        return CursorPage.FromOverfetch(rows, limit, r => r.Id);
    }

    public async Task<IReadOnlyList<ActorRow>> FindActorsByHandlesAsync(IReadOnlyCollection<string> normalizedHandles, CancellationToken cancellationToken) =>
        await BeepQueries.VisibleActors(db)
            .Where(a => a.NormalizedHandle != null && normalizedHandles.Contains(a.NormalizedHandle))
            .Select(BeepQueries.ToActorRow)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, Guid>> EnsureHashtagsAsync(IReadOnlyCollection<string> names, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var distinct = names.Distinct(StringComparer.Ordinal).ToArray();
        var ids = distinct.Select(_ => Guid.CreateVersion7(now)).ToArray();
        // Concurrent posts with the same new tag: one insert wins, the other waits for it and skips.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO hashtags (id, name, created_at)
            SELECT t.id, t.name, {now} FROM unnest({ids}::uuid[], {distinct}::text[]) AS t(id, name)
            ON CONFLICT (name) DO NOTHING
            """,
            cancellationToken);
        return await db.Set<Hashtag>()
            .AsNoTracking()
            .Where(h => distinct.Contains(h.Name))
            .ToDictionaryAsync(h => h.Name, h => h.Id, StringComparer.Ordinal, cancellationToken);
    }

    public void AddBeep(Beep beep, IReadOnlyCollection<Guid> mentionedActorIds, IReadOnlyCollection<Guid> hashtagIds)
    {
        ArgumentNullException.ThrowIfNull(beep);
        ArgumentNullException.ThrowIfNull(mentionedActorIds);
        ArgumentNullException.ThrowIfNull(hashtagIds);
        db.Set<Beep>().Add(beep);
        db.Set<Mention>().AddRange(mentionedActorIds.Select(id => new Mention { BeepId = beep.Id, ActorId = id }));
        db.Set<BeepHashtag>().AddRange(hashtagIds.Select(id => new BeepHashtag { BeepId = beep.Id, HashtagId = id, CreatedAt = beep.CreatedAt }));
    }

    public async Task<bool> DeleteAsync(Guid beepId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Set<Beep>()
            .Where(b => b.Id == beepId && b.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.DeletedAt, now), cancellationToken) == 1;

    public async Task<bool> AddLikeAsync(Guid actorId, Guid beepId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO likes (beep_id, actor_id, created_at) VALUES ({beepId}, {actorId}, {now}) ON CONFLICT DO NOTHING",
            cancellationToken) == 1;

    public async Task<bool> RemoveLikeAsync(Guid actorId, Guid beepId, CancellationToken cancellationToken) =>
        await db.Set<Like>().Where(l => l.BeepId == beepId && l.ActorId == actorId).ExecuteDeleteAsync(cancellationToken) == 1;

    public async Task<IReadOnlyDictionary<Guid, BeepCounts>> RecountAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken)
    {
        await db.Set<Beep>()
            .Where(b => beepIds.Contains(b.Id))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(b => b.LikeCount, b => db.Set<Like>().Count(l => l.BeepId == b.Id))
                    .SetProperty(b => b.ReplyCount, b => db.Set<Beep>().Count(r => r.ReplyToId == b.Id && r.DeletedAt == null && r.ModerationStatus != ModerationStatus.Hidden)),
                cancellationToken);
        return await db.Set<Beep>()
            .AsNoTracking()
            .Where(b => beepIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => new BeepCounts(b.LikeCount, b.ReplyCount), cancellationToken);
    }
}
