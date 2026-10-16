using Beeps.Features.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfSessionStore(BeepsDbContext db, HybridCache cache, TimeProvider time) : ISessionStore
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
            cancellationToken: cancellationToken);

    public async Task TouchAsync(SessionRow session, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await db.Set<UserSession>()
            .Where(s => s.Id == session.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now).SetProperty(x => x.ExpiresAt, expiresAt), cancellationToken);
        await cache.RemoveAsync(SharedCache.SessionKey(session.TokenHash), cancellationToken);
    }

    public async Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.Set<UserSession>()
            .Where(s => s.TokenHash == tokenHash && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
        await cache.RemoveAsync(SharedCache.SessionKey(tokenHash), cancellationToken);
    }

    public Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<UserSession>()
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);

    public async Task ForgetAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Expired sessions are rejected by their expiry anyway; only the rest can still be cached as live.
        var now = time.GetUtcNow();
        var hashes = await db.Set<UserSession>()
            .Where(s => s.UserId == userId && s.ExpiresAt > now)
            .Select(s => s.TokenHash)
            .ToListAsync(cancellationToken);
        await cache.RemoveAsync(hashes.Select(SharedCache.SessionKey), cancellationToken);
    }

    public Task<int> DeleteEndedAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken) =>
        db.Set<UserSession>()
            .Where(s => s.ExpiresAt < cutoff || s.RevokedAt < cutoff)
            .OrderBy(s => s.Id)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);
}
