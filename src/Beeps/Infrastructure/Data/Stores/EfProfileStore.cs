using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

/// <remarks>
/// Profiles and agent lists come from the content cache (<see cref="ContentReads"/>); follows, blocks and privacy settings
/// are asked of PostgreSQL.
/// </remarks>
public sealed class EfProfileStore(BeepsDbContext db, ContentReads reads, PendingCacheChanges cacheChanges) : IProfileStore
{
    /// <summary>
    /// {0} follower, {1} followee, {2} when. Not while either has blocked the other: asked again here, after the pair's
    /// lock, as a block that committed since the service asked has ended no follow yet (there was none to end).
    /// </summary>
    private static readonly string AddFollowSql = $$"""
        WITH added AS (
            INSERT INTO follows (follower_id, followee_id, created_at)
            SELECT {0}, {1}, {2}
            WHERE NOT EXISTS (SELECT 1 FROM blocks WHERE (blocker_id = {0} AND blocked_id = {1}) OR (blocker_id = {1} AND blocked_id = {0}))
            ON CONFLICT DO NOTHING
            RETURNING follower_id, followee_id)
        {{CountsChange("+", "added")}}
        """;

    /// <summary>{0} follower, {1} followee.</summary>
    private static readonly string RemoveFollowSql = $$"""
        WITH removed AS (
            DELETE FROM follows WHERE follower_id = {0} AND followee_id = {1}
            RETURNING follower_id, followee_id)
        {{CountsChange("-", "removed")}}
        """;

    /// <summary>{0} the humans whose following count to recount.</summary>
    private static readonly string RecountFollowingSql = $$"""
        UPDATE users AS u SET
            following_count = (SELECT count(*) FROM follows AS f WHERE f.follower_id = u.id AND {{Shown("f.followee_id")}})
        WHERE u.id = ANY ({0})
        """;

    /// <summary>{0} the humans whose followers count to recount.</summary>
    private static readonly string RecountFollowersSql = $$"""
        UPDATE users AS u SET
            followers_count = (SELECT count(*) FROM follows AS f WHERE f.followee_id = u.id AND {{Shown("f.follower_id")}})
        WHERE u.id = ANY ({0})
        """;

    public async Task<string?> FindHandleAsync(Guid actorId, CancellationToken cancellationToken) =>
        (await reads.GetActorsAsync([actorId], cancellationToken)).GetValueOrDefault(actorId) is { Visible: true } found ? found.Actor.Handle : null;

    public async Task<ProfileRow?> FindByHandleAsync(string normalizedHandle, CancellationToken cancellationToken)
    {
        var ids = await reads.GetActorIdsByHandleAsync([normalizedHandle], cancellationToken);
        if (!ids.TryGetValue(normalizedHandle, out var id))
        {
            return null;
        }

        var actors = await reads.GetActorsAsync([id], cancellationToken);
        if (actors.GetValueOrDefault(id) is not { Visible: true } found)
        {
            return null;
        }

        var actor = found.Actor;
        var owner = actor.OwnerId is { } ownerId ? (await reads.GetActorsAsync([ownerId], cancellationToken)).GetValueOrDefault(ownerId) : null;
        return new ProfileRow(found.Row, actor.Bio, actor.ModelName, actor.CreatedAt, owner?.Row, actor.BannerKey, actor.Website);
    }

    public async Task<IReadOnlyList<ActorRow>> ListAgentsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var ids = (await reads.GetAgentIdsAsync([ownerId], cancellationToken)).GetValueOrDefault(ownerId) ?? [];
        var agents = await reads.GetActorsAsync(ids, cancellationToken);
        // Connected (so they have a handle) and not deleted, oldest first.
        return [.. ids.Select(agents.GetValueOrDefault).Where(a => a is { Actor: { Deleted: false, Handle: not null } }).Select(a => a!.Row)];
    }

    public async Task<ProfileRelation> GetRelationAsync(Guid viewerId, Guid actorId, CancellationToken cancellationToken) =>
        await db.Set<Actor>()
            .Where(a => a.Id == actorId)
            .Select(a => new ProfileRelation(
                db.Set<Follow>().Any(f => f.FollowerId == viewerId && f.FolloweeId == actorId),
                db.Set<Block>().Any(k => k.BlockerId == viewerId && k.BlockedId == actorId)))
            .FirstOrDefaultAsync(cancellationToken) ?? ProfileRelation.None;

    public async Task<IReadOnlyList<Guid>> ListFollowedAsync(Guid followerId, CancellationToken cancellationToken) =>
        await db.Set<Follow>().Where(f => f.FollowerId == followerId).Select(f => f.FolloweeId).ToListAsync(cancellationToken);

    public Task<IReadOnlyList<Guid>> ListFollowedCachedAsync(Guid followerId, CancellationToken cancellationToken) =>
        reads.GetFollowedAsync(followerId, cancellationToken);

    public async Task<ProfilePrivacy> GetPrivacyAsync(Guid humanId, CancellationToken cancellationToken) =>
        await db.Set<User>()
            .Where(u => u.Id == humanId)
            .Select(u => new ProfilePrivacy(u.HideLikes, u.HideFollows, u.HideFollowCounts, new FollowCounts(u.FollowingCount, u.FollowersCount)))
            .FirstOrDefaultAsync(cancellationToken) ?? ProfilePrivacy.None;

    /// <remarks>The counts are kept on the human's row (<see cref="User.FollowingCount"/>): one primary-key read.</remarks>
    public async Task<FollowCounts> CountFollowsAsync(Guid humanId, CancellationToken cancellationToken) =>
        await db.Set<User>()
            .Where(u => u.Id == humanId)
            .Select(u => new FollowCounts(u.FollowingCount, u.FollowersCount))
            .FirstOrDefaultAsync(cancellationToken) ?? new FollowCounts(0, 0);

    public async Task<FollowCounterparts> ListFollowCounterpartsAsync(Guid humanId, CancellationToken cancellationToken) =>
        new(
            await db.Set<Follow>().Where(f => f.FolloweeId == humanId).Select(f => f.FollowerId).ToListAsync(cancellationToken),
            await db.Set<Follow>().Where(f => f.FollowerId == humanId).Select(f => f.FolloweeId).ToListAsync(cancellationToken));

    /// <remarks>
    /// The rows are locked first, in id order as a follow locks them, and counted in a statement of their own: its
    /// snapshot then has every follow that changed them, so a follow running meanwhile is neither lost nor counted twice.
    /// </remarks>
    public async Task RecountFollowsAsync(IReadOnlyCollection<Guid> followingOf, IReadOnlyCollection<Guid> followersOf, CancellationToken cancellationToken)
    {
        var ids = followingOf.Union(followersOf).ToArray();
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM users WHERE id = ANY ({ids}) ORDER BY id FOR NO KEY UPDATE",
            cancellationToken);
        if (followingOf.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(RecountFollowingSql, [followingOf.ToArray()], cancellationToken);
        }

        if (followersOf.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(RecountFollowersSql, [followersOf.ToArray()], cancellationToken);
        }
    }

    /// <remarks>
    /// Walks the <c>(follower_id, created_at, followee_id)</c> or <c>(followee_id, created_at, follower_id)</c> index newest
    /// first. The cursor is the other human of the last follow shown, whose follow gives the time to go on from; once that
    /// follow is gone (unfollowed since), the list ends there.
    /// </remarks>
    public async Task<CursorPage<PersonRow>> ListFollowsAsync(Guid humanId, FollowList list, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        var follows = list == FollowList.Followers
            ? db.Set<Follow>().Where(f => f.FolloweeId == humanId).Select(f => new { Id = f.FollowerId, f.CreatedAt })
            : db.Set<Follow>().Where(f => f.FollowerId == humanId).Select(f => new { Id = f.FolloweeId, f.CreatedAt });
        if (page.Before is { } before)
        {
            var at = follows.Where(f => f.Id == before).Select(f => (DateTimeOffset?)f.CreatedAt);
            // The first condition is the index range; the OR alone is only a filter, which read every page before this one.
            follows = follows.Where(f => f.CreatedAt <= at.FirstOrDefault() && (f.CreatedAt < at.FirstOrDefault() || f.Id < before));
        }

        var visible = BeepQueries.VisibleActors(db, viewerId);
        var ids = await follows
            .Where(f => visible.Any(a => a.Id == f.Id))
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.Id)
            .Take(page.Limit + 1)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);
        var found = CursorPage.FromOverfetch(ids, page.Limit, id => id);
        var actors = await reads.GetActorsAsync(found.Items, cancellationToken);
        // The query already decided they are visible to this reader; the entries are only for showing them.
        return new CursorPage<PersonRow>(
            [.. found.Items.Select(actors.GetValueOrDefault).OfType<ActorEntry>().Select(a => new PersonRow(a.Row, a.Actor.Bio))],
            found.NextCursor);
    }

    /// <remarks>
    /// Both humans' rows are locked first, in id order (as <see cref="RecountFollowsAsync"/> locks them), and the follow
    /// goes in with the counts in a statement of their own. Each count goes up only when the row went in and the other
    /// human is shown, as of after the lock: a ban that came first is seen, and its recount sees this follow.
    /// </remarks>
    public async Task<bool> AddFollowAsync(Guid followerId, Guid followeeId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await LockPairAsync(followerId, followeeId, cancellationToken);
        var added = await db.Database.ExecuteSqlRawAsync(AddFollowSql, [followerId, followeeId, now], cancellationToken);
        return FollowingChanged(followerId, added > 0);
    }

    /// <remarks>As <see cref="AddFollowAsync"/>: each count goes down only when the row went and the other human was counted.</remarks>
    public async Task<bool> RemoveFollowAsync(Guid followerId, Guid followeeId, CancellationToken cancellationToken)
    {
        await LockPairAsync(followerId, followeeId, cancellationToken);
        var removed = await db.Database.ExecuteSqlRawAsync(RemoveFollowSql, [followerId, followeeId], cancellationToken);
        return FollowingChanged(followerId, removed > 0);
    }

    public Task<bool> AnyBlockBetweenAsync(Guid humanId, Guid otherHumanId, CancellationToken cancellationToken) =>
        db.Set<Block>().AnyAsync(
            k => (k.BlockerId == humanId && k.BlockedId == otherHumanId) || (k.BlockerId == otherHumanId && k.BlockedId == humanId),
            cancellationToken);

    public async Task<bool> AddBlockAsync(Guid blockerId, Guid blockedId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO blocks (blocker_id, blocked_id, created_at) VALUES ({blockerId}, {blockedId}, {now}) ON CONFLICT DO NOTHING",
            cancellationToken) == 1;

    public async Task<bool> RemoveBlockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken) =>
        await db.Set<Block>().Where(k => k.BlockerId == blockerId && k.BlockedId == blockedId).ExecuteDeleteAsync(cancellationToken) == 1;

    /// <summary>
    /// SQL for "this human is shown" (<see cref="BeepVisibility.ActorIsVisible"/>, for a human): not deleted, not banned.
    /// The statements above are put together from these fragments once; only column names go in, never input.
    /// </summary>
    private static string Shown(string humanId) =>
        $"EXISTS (SELECT 1 FROM actors AS a JOIN users AS v ON v.id = a.id WHERE a.id = {humanId} AND a.deleted_at IS NULL AND v.banned_at IS NULL)";

    /// <summary>Both humans' counts, by one for the follow in <paramref name="changed"/> (none when no row changed).</summary>
    private static string CountsChange(string sign, string changed) =>
        $$"""
        UPDATE users AS u SET
            following_count = following_count {{sign}} CASE WHEN u.id = x.follower_id AND {{Shown("x.followee_id")}} THEN 1 ELSE 0 END,
            followers_count = followers_count {{sign}} CASE WHEN u.id = x.followee_id AND {{Shown("x.follower_id")}} THEN 1 ELSE 0 END
        FROM {{changed}} AS x
        WHERE u.id IN (x.follower_id, x.followee_id)
        """;

    /// <summary>Within the caller's transaction, until it ends.</summary>
    private async Task LockPairAsync(Guid humanId, Guid otherHumanId, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM users WHERE id IN ({humanId}, {otherHumanId}) ORDER BY id FOR NO KEY UPDATE",
            cancellationToken);

    private bool FollowingChanged(Guid followerId, bool changed)
    {
        if (changed)
        {
            cacheChanges.Add(CacheChange.Following(followerId));
        }

        return changed;
    }
}
