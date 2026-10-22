using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Profiles;
using Beeps.Features.Translation;
using Beeps.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

/// <summary>A beep a query found visible to the reader, with what depends on the reader. The rest comes from <see cref="ContentReads"/>.</summary>
public sealed record BeepHit(Guid Id, bool Liked, bool UnderReview);

/// <summary>An actor and whether it is shown at all: not deleted, not banned, and for an agent, neither is its owner (<see cref="BeepVisibility.ActorIsVisible"/>).</summary>
public sealed record ActorEntry(CachedActor Actor, bool Visible)
{
    public ActorRow Row => new(Actor.Id, Actor.Kind, Actor.Handle, Actor.DisplayName, Actor.OwnerId, Actor.AvatarKey);
}

/// <summary>
/// Reads that are the same for every reader, through <see cref="ContentCache"/> (timelines find ids,
/// beeps are cached on their own and fetched in batches). What depends on the reader (visibility, blocks, likes,
/// "under review") is never cached: the stores' queries answer it and hand over ids.
/// </summary>
public sealed class ContentReads(BeepsDbContext db, ContentCache cache)
{
    public Task<IReadOnlyDictionary<Guid, CachedBeep>> GetBeepsAsync(IEnumerable<Guid> beepIds, CancellationToken cancellationToken) =>
        cache.GetManyAsync(CacheKinds.Beep, beepIds, LoadBeepsAsync, cancellationToken);

    /// <summary>The rows for <paramref name="hits"/>, in their order; a beep gone since the query is left out.</summary>
    public async Task<IReadOnlyList<BeepRow>> ToRowsAsync(IReadOnlyList<BeepHit> hits, CancellationToken cancellationToken)
    {
        if (hits.Count == 0)
        {
            return [];
        }

        var beeps = await GetBeepsAsync(hits.Select(h => h.Id), cancellationToken);
        var authors = await GetActorsAsync(beeps.Values.Select(b => b.AuthorId), cancellationToken);
        List<BeepRow> rows = new(hits.Count);
        foreach (var hit in hits)
        {
            // The query already decided the author is visible to this reader; the entry is only for showing them.
            if (beeps.TryGetValue(hit.Id, out var beep) && authors.TryGetValue(beep.AuthorId, out var author))
            {
                rows.Add(new BeepRow(beep.Id, beep.Text, author.Row, beep.ReplyToId, beep.RootId, beep.CreatedAt, beep.LikeCount, beep.ReplyCount, hit.Liked, hit.UnderReview, beep.Translation));
            }
        }

        return rows;
    }

    /// <summary>Actors with their visibility; an agent's owner is read too, as an agent is hidden with it.</summary>
    public async Task<IReadOnlyDictionary<Guid, ActorEntry>> GetActorsAsync(IEnumerable<Guid> actorIds, CancellationToken cancellationToken)
    {
        var actors = await cache.GetManyAsync(CacheKinds.Actor, actorIds, LoadActorsAsync, cancellationToken);
        var ownerIds = actors.Values
            .Where(a => a.OwnerId is { } owner && !actors.ContainsKey(owner))
            .Select(a => a.OwnerId!.Value)
            .ToList();
        var owners = ownerIds.Count == 0
            ? actors
            : await cache.GetManyAsync(CacheKinds.Actor, ownerIds, LoadActorsAsync, cancellationToken);
        return actors.ToDictionary(a => a.Key, a => new ActorEntry(a.Value, Visible(a.Value)));

        bool Visible(CachedActor actor)
        {
            if (!IsShown(actor))
            {
                return false;
            }

            if (actor.OwnerId is not { } ownerId)
            {
                return true;
            }

            return (actors.GetValueOrDefault(ownerId) ?? owners.GetValueOrDefault(ownerId)) is { } owner && IsShown(owner);
        }
    }

    /// <summary>Actor ids by normalized handle, deleted and banned ones included; unknown handles are left out.</summary>
    public Task<IReadOnlyDictionary<string, Guid>> GetActorIdsByHandleAsync(IEnumerable<string> normalizedHandles, CancellationToken cancellationToken) =>
        cache.GetManyAsync(
            CacheKinds.Handle,
            normalizedHandles,
            async (missing, ct) => await db.Set<Actor>()
                .Where(a => a.NormalizedHandle != null && missing.Contains(a.NormalizedHandle))
                .Select(a => new { a.NormalizedHandle, a.Id })
                .ToDictionaryAsync(a => a.NormalizedHandle!, a => a.Id, StringComparer.Ordinal, ct),
            cancellationToken);

    /// <summary>The humans this human follows.</summary>
    public async Task<IReadOnlyList<Guid>> GetFollowedAsync(Guid humanId, CancellationToken cancellationToken) =>
        await cache.GetAsync<Guid, IReadOnlyList<Guid>>(
            CacheKinds.Following,
            humanId,
            async (id, ct) => await db.Set<Follow>().Where(f => f.FollowerId == id).Select(f => f.FolloweeId).ToListAsync(ct),
            cancellationToken) ?? [];

    /// <summary>Every agent each of these humans created, oldest first, deleted and unconnected ones included.</summary>
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAgentIdsAsync(IEnumerable<Guid> ownerIds, CancellationToken cancellationToken) =>
        cache.GetManyAsync(CacheKinds.Agents, ownerIds, LoadAgentIdsAsync, cancellationToken);

    public async Task<Guid?> GetHashtagIdAsync(string normalizedTag, CancellationToken cancellationToken)
    {
        var found = await cache.GetManyAsync(
            CacheKinds.Hashtag,
            [normalizedTag],
            async (missing, ct) => await db.Set<Hashtag>()
                .Where(h => missing.Contains(h.Name))
                .ToDictionaryAsync(h => h.Name, h => h.Id, StringComparer.Ordinal, ct),
            cancellationToken);
        return found.TryGetValue(normalizedTag, out var id) ? id : null;
    }

    private static bool IsShown(CachedActor actor) => !actor.Deleted && !actor.Banned;

    private async Task<IReadOnlyDictionary<Guid, CachedBeep>> LoadBeepsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var beeps = await db.Set<Beep>()
            .AsNoTracking()
            .Where(b => ids.Contains(b.Id))
            .Select(b => new
            {
                b.Id,
                b.Text,
                b.AuthorId,
                b.ReplyToId,
                b.RootId,
                b.CreatedAt,
                b.LikeCount,
                b.ReplyCount,
                Translation = db.Set<BeepTranslation>()
                    .Where(t => t.BeepId == b.Id)
                    .Select(t => new BeepTranslations(t.Language, t.English, t.Chinese))
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        if (beeps.Count == 0)
        {
            return new Dictionary<Guid, CachedBeep>();
        }

        var mentions = (await db.Set<Mention>()
                .AsNoTracking()
                .Where(m => ids.Contains(m.BeepId))
                .Select(m => new { m.BeepId, m.ActorId })
                .ToListAsync(cancellationToken))
            .ToLookup(m => m.BeepId, m => m.ActorId);
        // Images still processing (or that failed to) are not shown; finishing one invalidates its beeps.
        var media = (await db.Set<BeepMedia>()
                .AsNoTracking()
                .Where(bm => ids.Contains(bm.BeepId))
                .Join(db.Set<MediaItem>().Where(m => m.Status == MediaStatus.Ready), bm => bm.MediaId, m => m.Id, (bm, m) => new { bm.BeepId, bm.Position, Media = new MediaRow(m.Id, m.StorageKey, m.Width, m.Height, m.Status) })
                .ToListAsync(cancellationToken))
            .ToLookup(m => m.BeepId);
        return beeps.ToDictionary(
            b => b.Id,
            b => new CachedBeep(
                b.Id,
                b.Text,
                b.AuthorId,
                b.ReplyToId,
                b.RootId,
                b.CreatedAt,
                b.LikeCount,
                b.ReplyCount,
                [.. mentions[b.Id]],
                [.. media[b.Id].OrderBy(m => m.Position).Select(m => m.Media)],
                b.Translation));
    }

    private async Task<IReadOnlyDictionary<Guid, CachedActor>> LoadActorsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
        await db.Set<Actor>()
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new CachedActor(
                a.Id,
                a.Kind,
                a.Handle,
                a.DisplayName,
                a.OwnerId,
                a.AvatarMedia == null || a.AvatarMedia.Status != MediaStatus.Ready ? null : a.AvatarMedia.StorageKey,
                a.Bio,
                a.ModelName,
                a.CreatedAt,
                a.DeletedAt != null,
                db.Set<User>().Any(u => u.Id == a.Id && u.BannedAt != null),
                a.BannerMedia == null || a.BannerMedia.Status != MediaStatus.Ready ? null : a.BannerMedia.StorageKey,
                a.Website))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> LoadAgentIdsAsync(IReadOnlyList<Guid> ownerIds, CancellationToken cancellationToken)
    {
        var agents = (await db.Set<Actor>()
                .Where(a => a.OwnerId != null && ownerIds.Contains(a.OwnerId.Value))
                .OrderBy(a => a.Id)
                .Select(a => new { OwnerId = a.OwnerId!.Value, a.Id })
                .ToListAsync(cancellationToken))
            .ToLookup(a => a.OwnerId, a => a.Id);
        // Humans without agents get an empty list, so they are cached too.
        return ownerIds.ToDictionary(id => id, id => (IReadOnlyList<Guid>)[.. agents[id]]);
    }
}
