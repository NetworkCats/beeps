using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Media;
using Beeps.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfAgentStore(BeepsDbContext db, CacheInvalidator invalidator, PendingCacheChanges cacheChanges, TimeProvider time) : IAgentStore
{
    public Task<int> CountAgentsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Agents().CountAsync(a => a.OwnerId == ownerId, cancellationToken);

    public void AddAgent(Actor agent) => db.Set<Actor>().Add(agent);

    public void AddKey(AgentKey key) => db.Set<AgentKey>().Add(key);

    public async Task<IReadOnlyList<AgentRow>> ListAgentsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await Agents()
            .AsNoTracking()
            .Where(a => a.OwnerId == ownerId)
            .LeftJoin(db.Set<AgentKey>().Where(k => k.RevokedAt == null), a => a.Id, k => k.AgentId, (a, k) => new { a, k })
            .OrderBy(x => x.a.Id)
            .Select(x => new AgentRow(
                x.a.Id,
                x.a.Handle,
                x.a.DisplayName,
                x.a.ModelName,
                x.a.CreatedAt,
                x.a.DisabledAt,
                x.k == null ? null : x.k.KeyPrefix,
                x.k == null ? null : x.k.LastUsedAt,
                x.a.Bio,
                x.a.AvatarMedia == null || x.a.AvatarMedia.Status != MediaStatus.Ready ? null : x.a.AvatarMedia.StorageKey))
            .ToListAsync(cancellationToken);

    public Task<Actor?> FindAgentAsync(Guid agentId, CancellationToken cancellationToken) =>
        Agents().FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);

    public Task ReloadAsync(Actor agent, CancellationToken cancellationToken) => db.Entry(agent).ReloadAsync(cancellationToken);

    public Task RevokeKeysAsync(Guid agentId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<AgentKey>()
            .Where(k => k.AgentId == agentId && k.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, now), cancellationToken);

    public Task<AgentKeyMatch?> FindActiveKeyAsync(string keyHash, CancellationToken cancellationToken) =>
        invalidator.GetSharedAsync(
            SharedCache.AgentKeyKey(keyHash),
            (db, keyHash),
            static (state, ct) => QueryActiveKeyAsync(state.db, state.keyHash, ct),
            SharedCache.AgentKey);

    public async Task TouchKeyAsync(AgentKeyMatch key, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        await db.Set<AgentKey>()
            .Where(k => k.Id == key.KeyId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), cancellationToken);
        await invalidator.ForgetSharedAsync([SharedCache.AgentKeyKey(key.KeyHash)]);
    }

    public async Task ForgetKeysAsync(IReadOnlyCollection<Guid> agentIds)
    {
        ArgumentNullException.ThrowIfNull(agentIds);
        if (agentIds.Count == 0)
        {
            return;
        }

        // A key revoked longer ago than its cached copy lives is cached as revoked, if at all: only the rest can still be
        // cached as working (the list would otherwise grow with every key ever made).
        var revokedSince = time.GetUtcNow() - 2 * SharedCache.AgentKey.Expiration!.Value;
        var hashes = await db.Set<AgentKey>()
            .Where(k => agentIds.Contains(k.AgentId) && (k.RevokedAt == null || k.RevokedAt > revokedSince))
            .Select(k => k.KeyHash)
            .ToListAsync(CancellationToken.None);
        await invalidator.ForgetSharedAsync([.. hashes.Select(SharedCache.AgentKeyKey)]);
    }

    public async Task<IReadOnlyList<Guid>> DisableAllAsync(Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ids = await Agents().Where(a => a.OwnerId == ownerId).Select(a => a.Id).ToListAsync(cancellationToken);
        await Agents()
            .Where(a => a.OwnerId == ownerId && a.DisabledAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DisabledAt, now), cancellationToken);
        return ids;
    }

    public async Task<IReadOnlyList<Guid>> DeleteAllAsync(Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ids = await Agents().Where(a => a.OwnerId == ownerId).Select(a => a.Id).ToListAsync(cancellationToken);
        await Agents()
            .Where(a => a.OwnerId == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeletedAt, now), cancellationToken);
        await db.Set<AgentKey>()
            .Where(k => ids.Contains(k.AgentId) && k.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, now), cancellationToken);
        cacheChanges.Add(ids.Select(CacheChange.ActorVisibility));
        return ids;
    }

    public Task<AgentWithOwnerRow?> GetAgentWithOwnerAsync(Guid agentId, CancellationToken cancellationToken) =>
        Agents()
            .Where(a => a.Id == agentId)
            .Join(db.Set<Actor>(), a => a.OwnerId, o => o.Id, (a, o) => new AgentWithOwnerRow(
                a.Id,
                a.Handle,
                a.DisplayName,
                a.ModelName,
                a.CreatedAt,
                o.Id,
                o.Handle!,
                o.DisplayName!,
                a.AvatarMedia == null || a.AvatarMedia.Status != MediaStatus.Ready ? null : a.AvatarMedia.StorageKey,
                o.AvatarMedia == null || o.AvatarMedia.Status != MediaStatus.Ready ? null : o.AvatarMedia.StorageKey))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> HandleTakenAsync(string normalizedHandle, CancellationToken cancellationToken) =>
        db.Set<Actor>().AnyAsync(a => a.NormalizedHandle == normalizedHandle, cancellationToken);

    public async Task<IReadOnlySet<string>> ListHandlesStartingWithAsync(string normalizedPrefix, CancellationToken cancellationToken) =>
        (await db.Set<Actor>()
            .Where(a => EF.Functions.Like(a.NormalizedHandle!, SqlLike.Prefix(normalizedPrefix), SqlLike.Escape))
            .Select(a => a.NormalizedHandle!)
            .ToListAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

    public async Task<bool> TryConnectAsync(Guid agentId, string modelName, string displayName, string handle, CancellationToken cancellationToken)
    {
        var connected = await Agents()
            .Where(a => a.Id == agentId && a.ModelName == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(a => a.ModelName, modelName)
                    .SetProperty(a => a.DisplayName, displayName)
                    .SetProperty(a => a.Handle, handle)
                    .SetProperty(a => a.NormalizedHandle, AccountRules.NormalizeHandle(handle)),
                cancellationToken) == 1;
        if (connected)
        {
            // Its new handle had no entry (missing handles are not cached); its owner's agent list already has it.
            cacheChanges.Add(CacheChange.AgentConnected(agentId));
        }

        return connected;
    }

    public Task<bool> IsKeyActiveAsync(Guid keyId, CancellationToken cancellationToken) =>
        ActiveKeys(db).AnyAsync(x => x.Key.Id == keyId, cancellationToken);

    private static Task<AgentKeyMatch?> QueryActiveKeyAsync(BeepsDbContext db, string keyHash, CancellationToken cancellationToken) =>
        ActiveKeys(db)
            .Where(x => x.Key.KeyHash == keyHash)
            .Select(x => new AgentKeyMatch(x.Key.Id, x.Key.KeyHash, x.Agent.Id, x.Owner.Id, x.Key.LastUsedAt, x.Agent.ModelName != null))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Keys that work: not revoked, of an agent neither disabled nor deleted, whose owner is neither banned nor deleted.</summary>
    private static IQueryable<ActiveKey> ActiveKeys(BeepsDbContext db) =>
        db.Set<AgentKey>()
            .Where(k => k.RevokedAt == null)
            .Join(db.Set<Actor>(), k => k.AgentId, a => a.Id, (k, a) => new { k, a })
            .Where(x => x.a.DisabledAt == null && x.a.DeletedAt == null)
            .Join(db.Set<User>(), x => x.a.OwnerId, u => u.Id, (x, u) => new { x.k, x.a, u })
            .Where(x => x.u.BannedAt == null && x.u.Actor.DeletedAt == null)
            .Select(x => new ActiveKey { Key = x.k, Agent = x.a, Owner = x.u });

    /// <summary>An initializer, not a constructor, so EF can keep filtering on it.</summary>
    private sealed class ActiveKey
    {
        public required AgentKey Key { get; init; }

        public required Actor Agent { get; init; }

        public required User Owner { get; init; }
    }

    private IQueryable<Actor> Agents() =>
        db.Set<Actor>().Where(a => a.Kind == ActorKind.Agent && a.DeletedAt == null);
}
