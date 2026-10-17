using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

/// <remarks>Beeps, mentioned actors and images are read through the content cache (<see cref="ContentReads"/>); which beeps a reader may see is always asked of PostgreSQL.</remarks>
public sealed class EfBeepStore(BeepsDbContext db, ContentReads reads, PendingCacheChanges cacheChanges) : IBeepStore
{
    public Task<BeepRef?> FindVisibleAsync(Guid beepId, Guid? viewerId, CancellationToken cancellationToken) =>
        BeepQueries.Visible(db, viewerId)
            .Where(b => b.Id == beepId)
            .Select(b => new BeepRef(b.Id, b.AuthorId, b.Author.OwnerId, b.RootId, b.ReplyToId, b.LikeCount, b.ReplyCount))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BeepRow>> GetRowsAsync(IReadOnlyCollection<Guid> beepIds, Guid? viewerId, CancellationToken cancellationToken)
    {
        if (beepIds.Count == 0)
        {
            return [];
        }

        var hits = await BeepQueries.Visible(db, viewerId).Where(b => beepIds.Contains(b.Id)).Hits(db, viewerId).ToListAsync(cancellationToken);
        return await reads.ToRowsAsync(hits, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ActorRow>>> GetMentionsAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken)
    {
        var beeps = await reads.GetBeepsAsync(beepIds, cancellationToken);
        var mentioned = beeps.Values.SelectMany(b => b.Mentions).ToList();
        if (mentioned.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ActorRow>>();
        }

        var actors = await reads.GetActorsAsync(mentioned, cancellationToken);
        return beeps.Values
            .Select(b => (b.Id, Actors: b.Mentions.Select(actors.GetValueOrDefault).Where(a => a is { Visible: true }).Select(a => a!.Row).ToList()))
            .Where(b => b.Actors.Count > 0)
            .ToDictionary(b => b.Id, b => (IReadOnlyList<ActorRow>)b.Actors);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<MediaRow>>> GetMediaAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken) =>
        (await reads.GetBeepsAsync(beepIds, cancellationToken)).Values
            .Where(b => b.Media.Count > 0)
            .ToDictionary(b => b.Id, b => b.Media);

    public async Task<CursorPage<BeepRow>> ListThreadAsync(Guid rootId, Guid? after, int limit, Guid? viewerId, CancellationToken cancellationToken)
    {
        var thread = BeepQueries.Visible(db, viewerId).Where(b => b.RootId == rootId);
        if (after is { } cursor)
        {
            thread = thread.Where(b => b.Id > cursor);
        }

        var hits = await thread.OrderBy(b => b.Id).Take(limit + 1).Hits(db, viewerId).ToListAsync(cancellationToken);
        return await BeepQueries.ToPageAsync(reads, hits, limit, cancellationToken);
    }

    public Task<bool> IsBlockedByAsync(Guid blockerId, Guid actorId, Guid accountId, CancellationToken cancellationToken) =>
        db.Set<Block>().AnyAsync(k => k.BlockerId == blockerId && (k.BlockedId == actorId || k.BlockedId == accountId), cancellationToken);

    public async Task<IReadOnlyList<ActorRow>> FindActorsByHandlesAsync(IReadOnlyCollection<string> normalizedHandles, CancellationToken cancellationToken)
    {
        var ids = await reads.GetActorIdsByHandleAsync(normalizedHandles, cancellationToken);
        var actors = await reads.GetActorsAsync(ids.Values, cancellationToken);
        return [.. actors.Values.Where(a => a.Visible && a.Actor.Handle is not null).Select(a => a.Row)];
    }

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

    public void AttachMedia(Guid beepId, IReadOnlyList<Guid> mediaIds)
    {
        ArgumentNullException.ThrowIfNull(mediaIds);
        db.Set<BeepMedia>().AddRange(mediaIds.Select((id, position) => new BeepMedia { BeepId = beepId, MediaId = id, Position = position }));
    }

    public async Task<bool> DeleteAsync(Guid beepId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var deleted = await db.Set<Beep>()
            .Where(b => b.Id == beepId && b.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.DeletedAt, now), cancellationToken) == 1;
        if (deleted)
        {
            cacheChanges.Add(CacheChange.BeepRemoved(beepId));
        }

        return deleted;
    }

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
