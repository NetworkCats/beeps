using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Profiles;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfProfileStore(BeepsDbContext db) : IProfileStore
{
    public Task<ProfileRow?> FindByHandleAsync(string normalizedHandle, CancellationToken cancellationToken) =>
        BeepQueries.VisibleActors(db)
            .Where(a => a.NormalizedHandle == normalizedHandle)
            .LeftJoin(db.Set<Actor>(), a => a.OwnerId, o => (Guid?)o.Id, (a, o) => new ProfileRow(
                new ActorRow(a.Id, a.Kind, a.Handle, a.DisplayName, a.OwnerId, a.AvatarMedia == null || a.AvatarMedia.Status != MediaStatus.Ready ? null : a.AvatarMedia.StorageKey),
                a.Bio,
                a.ModelName,
                a.CreatedAt,
                o == null ? null : new ActorRow(o.Id, o.Kind, o.Handle, o.DisplayName, o.OwnerId, o.AvatarMedia == null || o.AvatarMedia.Status != MediaStatus.Ready ? null : o.AvatarMedia.StorageKey)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ActorRow>> ListAgentsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.Set<Actor>()
            .AsNoTracking()
            .Where(a => a.OwnerId == ownerId && a.DeletedAt == null && a.Handle != null)
            .OrderBy(a => a.Id)
            .Select(BeepQueries.ToActorRow)
            .ToListAsync(cancellationToken);

    public Task<bool> IsFollowingAsync(Guid followerId, Guid followeeId, CancellationToken cancellationToken) =>
        db.Set<Follow>().AnyAsync(f => f.FollowerId == followerId && f.FolloweeId == followeeId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListFollowedAsync(Guid followerId, CancellationToken cancellationToken) =>
        await db.Set<Follow>().Where(f => f.FollowerId == followerId).Select(f => f.FolloweeId).ToListAsync(cancellationToken);

    public async Task<bool> AddFollowAsync(Guid followerId, Guid followeeId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO follows (follower_id, followee_id, created_at) VALUES ({followerId}, {followeeId}, {now}) ON CONFLICT DO NOTHING",
            cancellationToken) == 1;

    public async Task<bool> RemoveFollowAsync(Guid followerId, Guid followeeId, CancellationToken cancellationToken) =>
        await db.Set<Follow>().Where(f => f.FollowerId == followerId && f.FolloweeId == followeeId).ExecuteDeleteAsync(cancellationToken) == 1;

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
}
