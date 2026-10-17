using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

/// <remarks>Profiles and agent lists come from the content cache (<see cref="ContentReads"/>); follows and blocks are asked of PostgreSQL.</remarks>
public sealed class EfProfileStore(BeepsDbContext db, ContentReads reads, PendingCacheChanges cacheChanges) : IProfileStore
{
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
        return new ProfileRow(found.Row, actor.Bio, actor.ModelName, actor.CreatedAt, owner?.Row);
    }

    public async Task<IReadOnlyList<ActorRow>> ListAgentsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var ids = (await reads.GetAgentIdsAsync([ownerId], cancellationToken)).GetValueOrDefault(ownerId) ?? [];
        var agents = await reads.GetActorsAsync(ids, cancellationToken);
        // Connected (so they have a handle) and not deleted, oldest first.
        return [.. ids.Select(agents.GetValueOrDefault).Where(a => a is { Actor: { Deleted: false, Handle: not null } }).Select(a => a!.Row)];
    }

    public Task<bool> IsFollowingAsync(Guid followerId, Guid followeeId, CancellationToken cancellationToken) =>
        db.Set<Follow>().AnyAsync(f => f.FollowerId == followerId && f.FolloweeId == followeeId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListFollowedAsync(Guid followerId, CancellationToken cancellationToken) =>
        await db.Set<Follow>().Where(f => f.FollowerId == followerId).Select(f => f.FolloweeId).ToListAsync(cancellationToken);

    public async Task<bool> AddFollowAsync(Guid followerId, Guid followeeId, DateTimeOffset now, CancellationToken cancellationToken) =>
        FollowingChanged(followerId, await db.Database.ExecuteSqlAsync(
            $"INSERT INTO follows (follower_id, followee_id, created_at) VALUES ({followerId}, {followeeId}, {now}) ON CONFLICT DO NOTHING",
            cancellationToken) == 1);

    public async Task<bool> RemoveFollowAsync(Guid followerId, Guid followeeId, CancellationToken cancellationToken) =>
        FollowingChanged(followerId, await db.Set<Follow>().Where(f => f.FollowerId == followerId && f.FolloweeId == followeeId).ExecuteDeleteAsync(cancellationToken) == 1);

    public Task<bool> IsBlockingAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken) =>
        db.Set<Block>().AnyAsync(k => k.BlockerId == blockerId && k.BlockedId == blockedId, cancellationToken);

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

    private bool FollowingChanged(Guid followerId, bool changed)
    {
        if (changed)
        {
            cacheChanges.Add(CacheChange.Following(followerId));
        }

        return changed;
    }
}
