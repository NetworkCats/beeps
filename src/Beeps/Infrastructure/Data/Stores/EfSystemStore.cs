using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Beeps;
using Beeps.Features.Moderation;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Jobs;
using Beeps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfSystemStore(BeepsDbContext db, HybridCache cache) : ISystemStore
{
    /// <summary>
    /// The totals scan whole tables (a parallel scan of every beep), so every open admin page shares one count for a
    /// minute; the admin pages follow them live (AdminFiguresWorker, every few seconds), so that is how far behind they
    /// may be. Today's counts, which is where the changes show, stay exact.
    /// </summary>
    private static readonly HybridCacheEntryOptions TotalsCache = new() { Expiration = TimeSpan.FromMinutes(1), LocalCacheExpiration = TimeSpan.FromMinutes(1) };

    /// <remarks>
    /// "New" compares ids rather than creation times: UUIDv7 ids sort by time, so the primary keys answer it.
    /// The totals count whole tables, so they are cached for a minute; today's counts and the backlogs are exact.
    /// </remarks>
    public async Task<SiteCounts> CountAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        // The smallest UUIDv7 of that millisecond: every id created since compares greater or equal.
        var first = FirstIdAt(since);
        var humans = db.Set<User>().Where(u => u.Actor.DeletedAt == null);
        var agents = db.Set<Actor>().Where(a => a.Kind == ActorKind.Agent && a.DeletedAt == null);
        var beeps = db.Set<Beep>().Where(b => b.DeletedAt == null);
        var totals = await cache.GetOrCreateAsync(
            SharedCache.SiteTotalsKey,
            (humans, agents, beeps),
            static async (q, ct) => new SiteTotals(await q.humans.CountAsync(ct), await q.agents.CountAsync(ct), await q.beeps.CountAsync(ct)),
            TotalsCache,
            cancellationToken: cancellationToken);
        return new SiteCounts(
            totals.Users,
            totals.Agents,
            totals.Beeps,
            await humans.CountAsync(u => u.Id >= first, cancellationToken),
            await agents.CountAsync(a => a.Id >= first, cancellationToken),
            await beeps.CountAsync(b => b.Id >= first, cancellationToken),
            await db.Set<ModerationJob>().CountAsync(cancellationToken),
            await db.Set<OutboxMessage>().CountAsync(cancellationToken),
            await db.Set<OutboxDeadMessage>().CountAsync(cancellationToken));
    }

    public async Task<UnannouncedCounts> CountUnannouncedAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var first = FirstIdAt(since);
        return new UnannouncedCounts(
            await db.Set<User>().CountAsync(u => u.Actor.DeletedAt == null && u.Id >= first, cancellationToken),
            await db.Set<ModerationJob>().CountAsync(cancellationToken),
            await db.Set<OutboxMessage>().CountAsync(cancellationToken),
            await db.Set<OutboxDeadMessage>().CountAsync(cancellationToken));
    }

    public async Task<CursorPage<DeadJobRow>> ListDeadJobsAsync(Guid? before, int limit, CancellationToken cancellationToken)
    {
        var dead = db.Set<OutboxDeadMessage>().AsNoTracking();
        if (before is { } cursor)
        {
            dead = dead.Where(m => m.Id < cursor);
        }

        var rows = await dead
            .OrderByDescending(m => m.Id)
            .Take(limit + 1)
            .Select(m => new DeadJobRow(m.Id, m.Type, m.Attempts, m.LastError, m.CreatedAt, m.FailedAt))
            .ToListAsync(cancellationToken);
        return CursorPage.FromOverfetch(rows, limit, r => r.Id);
    }

    public async Task<bool> RetryDeadJobAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
        // The outbox trigger wakes the workers once this commits.
        await db.Database.ExecuteSqlAsync(
            $"""
            WITH moved AS (
                DELETE FROM outbox_dead WHERE id = {id}
                RETURNING id, type, payload, last_error, created_at
            )
            INSERT INTO outbox (id, type, payload, attempts, run_after, created_at, last_error)
            SELECT id, type, payload, 0, {now}, created_at, last_error FROM moved
            """,
            cancellationToken) == 1;

    public async Task<bool> DeleteDeadJobAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Set<OutboxDeadMessage>().Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken) == 1;

    public async Task<IReadOnlyList<ScheduledJobRow>> ListScheduledJobsAsync(CancellationToken cancellationToken) =>
        await db.Set<ScheduledJobState>()
            .AsNoTracking()
            .OrderBy(j => j.Name)
            .Select(j => new ScheduledJobRow(j.Name, j.LastStartedAt, j.LastFinishedAt, j.LastError))
            .ToListAsync(cancellationToken);

    /// <summary>A UUIDv7 with the given time and every other bit zero.</summary>
    private static Guid FirstIdAt(DateTimeOffset time)
    {
        Span<byte> bytes = stackalloc byte[16];
        var milliseconds = time.ToUnixTimeMilliseconds();
        for (var i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)milliseconds;
            milliseconds >>= 8;
        }

        bytes[6] = 0x70;
        bytes[8] = 0x80;
        return new Guid(bytes, bigEndian: true);
    }
}

/// <summary>Cached by <see cref="EfSystemStore.CountAsync"/>.</summary>
public sealed record SiteTotals(int Users, int Agents, int Beeps);
