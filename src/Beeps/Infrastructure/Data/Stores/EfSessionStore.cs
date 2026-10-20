using Beeps.Features.Accounts;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfSessionStore(BeepsDbContext db, HybridCache cache, CacheInvalidator invalidator, IOutbox outbox, TimeProvider time) : ISessionStore
{
    public void Add(UserSession session) => db.Set<UserSession>().Add(session);

    public async Task<SessionRow?> FindAsync(string tokenHash, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            SharedCache.SessionKey(tokenHash),
            (db, tokenHash),
            static async (state, ct) => await state.db.Set<UserSession>()
                .Where(s => s.TokenHash == state.tokenHash && s.RevokedAt == null)
                .Select(s => new SessionRow(s.Id, s.UserId, s.TokenHash, s.LastSeenAt, s.ExpiresAt))
                .FirstOrDefaultAsync(ct),
            SharedCache.Session,
            // Not cancelled with the request: the loader runs on this request's context, so the request must wait for it (a
            // cancellable caller returns early, and its pooled context would go to another request mid-query).
            cancellationToken: CancellationToken.None);

    public async Task TouchAsync(SessionRow session, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await db.Set<UserSession>()
            .Where(s => s.Id == session.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now).SetProperty(x => x.ExpiresAt, expiresAt), cancellationToken);
        await invalidator.ForgetSharedAsync([SharedCache.SessionKey(session.TokenHash)]);
    }

    public Task<bool> IsActiveAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<UserSession>().AnyAsync(s => s.Id == sessionId && s.RevokedAt == null && s.ExpiresAt > now, cancellationToken);

    public async Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Committed with the durable copy of its removal from the cache (StageForget), then removed at once.
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var revoked = await db.Set<UserSession>()
                .Where(s => s.TokenHash == tokenHash && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
            if (revoked > 0)
            {
                StageForget([SharedCache.SessionKey(tokenHash)]);
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await invalidator.ForgetSharedAsync([SharedCache.SessionKey(tokenHash)]);
    }

    public async Task<bool> RevokeByIdAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hash = await db.Set<UserSession>()
            .Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .Select(s => s.TokenHash)
            .FirstOrDefaultAsync(cancellationToken);
        if (hash is null)
        {
            return false;
        }

        await RevokeAsync(hash, now, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ActiveSession>> ListActiveAsync(Guid userId, Guid? currentSessionId, DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await db.Set<UserSession>()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.Id == currentSessionId)
            .ThenByDescending(s => s.LastSeenAt)
            .ThenByDescending(s => s.Id)
            .Take(limit)
            .Select(s => new ActiveSession(s.Id, s.CreatedAt, s.LastSeenAt, s.UserAgent, s.IpAddress))
            .ToListAsync(cancellationToken);

    public Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        RevokeWhereAsync(userId, db.Set<UserSession>().Where(s => s.UserId == userId && s.RevokedAt == null), now, cancellationToken);

    public Task RevokeOthersAsync(Guid userId, Guid keepSessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        RevokeWhereAsync(userId, db.Set<UserSession>().Where(s => s.UserId == userId && s.Id != keepSessionId && s.RevokedAt == null), now, cancellationToken);

    /// <summary>
    /// Revokes the sessions and stages the durable copy of their removal from the cache, with the account's state (a
    /// ban is part of it), for the caller to save in the same transaction (<see cref="ForgetAllAsync"/> removes them at
    /// once after the commit; this one, if the instance stops before that).
    /// </summary>
    private async Task RevokeWhereAsync(Guid userId, IQueryable<UserSession> sessions, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hashes = await sessions.Where(s => s.ExpiresAt > now).Select(s => s.TokenHash).ToListAsync(cancellationToken);
        await sessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
        StageForget([.. hashes.Select(SharedCache.SessionKey), SharedCache.AccountKey(userId)]);
    }

    private void StageForget(IReadOnlyList<string> keys) =>
        outbox.Enqueue(SharedCacheForgetRequest.MessageType, new SharedCacheForgetRequest(keys), ContentCacheJsonContext.Default.SharedCacheForgetRequest);

    public Task ForgetAllAsync(Guid userId) => ForgetUsersAsync([userId]);

    public async Task ForgetUsersAsync(IReadOnlyCollection<Guid> userIds)
    {
        if (userIds.Count == 0)
        {
            return;
        }

        // Expired sessions are rejected by their expiry anyway, and one revoked longer ago than its cached copy lives is
        // cached as revoked, if at all; only the rest can still be cached as live.
        var now = time.GetUtcNow();
        var revokedSince = now - 2 * SharedCache.Session.Expiration!.Value;
        var hashes = await db.Set<UserSession>()
            .Where(s => userIds.Contains(s.UserId) && s.ExpiresAt > now && (s.RevokedAt == null || s.RevokedAt > revokedSince))
            .Select(s => s.TokenHash)
            .ToListAsync(CancellationToken.None);
        await invalidator.ForgetSharedAsync([.. hashes.Select(SharedCache.SessionKey)]);
    }

    public Task<int> DeleteEndedAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken) =>
        db.Set<UserSession>()
            .Where(s => s.ExpiresAt < cutoff || s.RevokedAt < cutoff)
            .OrderBy(s => s.Id)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);
}
