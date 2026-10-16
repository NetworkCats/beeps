using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfAgentStore(BeepsDbContext db, HybridCache cache) : IAgentStore
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
                x.k == null ? null : x.k.CreatedAt,
                x.k == null ? null : x.k.LastUsedAt))
            .ToListAsync(cancellationToken);

    public Task<Actor?> FindAgentAsync(Guid agentId, CancellationToken cancellationToken) =>
        Agents().FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);

    public Task RevokeKeysAsync(Guid agentId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<AgentKey>()
            .Where(k => k.AgentId == agentId && k.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, now), cancellationToken);

    public async Task<AgentKeyMatch?> FindActiveKeyAsync(string keyHash, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            SharedCache.AgentKeyKey(keyHash),
            (db, keyHash),
            static async (state, ct) => await QueryActiveKeyAsync(state.db, state.keyHash, ct),
            SharedCache.AgentKey,
            cancellationToken: cancellationToken);

    public async Task TouchKeyAsync(AgentKeyMatch key, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        await db.Set<AgentKey>()
            .Where(k => k.Id == key.KeyId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), cancellationToken);
        await cache.RemoveAsync(SharedCache.AgentKeyKey(key.KeyHash), cancellationToken);
    }

    public async Task ForgetKeysAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentIds);
        if (agentIds.Count == 0)
        {
            return;
        }

        var hashes = await db.Set<AgentKey>()
            .Where(k => agentIds.Contains(k.AgentId))
            .Select(k => k.KeyHash)
            .ToListAsync(cancellationToken);
        await cache.RemoveAsync(hashes.Select(SharedCache.AgentKeyKey), cancellationToken);
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
                o.DisplayName!))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlySet<string>> ListHandlesStartingWithAsync(string normalizedPrefix, CancellationToken cancellationToken) =>
        (await db.Set<Actor>()
            .Where(a => EF.Functions.Like(a.NormalizedHandle!, SqlLike.Prefix(normalizedPrefix), SqlLike.Escape))
            .Select(a => a.NormalizedHandle!)
            .ToListAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

    public async Task<bool> TryConnectAsync(Guid agentId, string modelName, string displayName, string handle, CancellationToken cancellationToken) =>
        await Agents()
            .Where(a => a.Id == agentId && a.ModelName == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(a => a.ModelName, modelName)
                    .SetProperty(a => a.DisplayName, displayName)
                    .SetProperty(a => a.Handle, handle)
                    .SetProperty(a => a.NormalizedHandle, AccountRules.NormalizeHandle(handle)),
                cancellationToken) == 1;

    private static Task<AgentKeyMatch?> QueryActiveKeyAsync(BeepsDbContext db, string keyHash, CancellationToken cancellationToken) =>
        db.Set<AgentKey>()
            .Where(k => k.KeyHash == keyHash && k.RevokedAt == null)
            .Join(db.Set<Actor>(), k => k.AgentId, a => a.Id, (k, a) => new { k, a })
            .Where(x => x.a.DisabledAt == null && x.a.DeletedAt == null)
            .Join(db.Set<User>(), x => x.a.OwnerId, u => u.Id, (x, u) => new { x.k, x.a, u })
            .Where(x => x.u.BannedAt == null && x.u.Actor.DeletedAt == null)
            .Select(x => new AgentKeyMatch(x.k.Id, x.k.KeyHash, x.a.Id, x.u.Id, x.k.LastUsedAt, x.a.ModelName != null))
            .FirstOrDefaultAsync(cancellationToken);

    private IQueryable<Actor> Agents() =>
        db.Set<Actor>().Where(a => a.Kind == ActorKind.Agent && a.DeletedAt == null);
}
