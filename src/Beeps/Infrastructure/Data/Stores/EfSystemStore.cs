using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Beeps;
using Beeps.Features.Moderation;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Jobs;
using Beeps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfSystemStore(BeepsDbContext db) : ISystemStore
{
    /// <remarks>
    /// "New" compares ids rather than creation times: UUIDv7 ids sort by time, so the primary keys answer it.
    /// The totals count whole tables, which is fine for one admin page at a time.
    /// </remarks>
    public async Task<SiteCounts> CountAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        // The smallest UUIDv7 of that millisecond: every id created since compares greater or equal.
        var first = FirstIdAt(since);
        var humans = db.Set<User>().Where(u => u.Actor.DeletedAt == null);
        var agents = db.Set<Actor>().Where(a => a.Kind == ActorKind.Agent && a.DeletedAt == null);
        var beeps = db.Set<Beep>().Where(b => b.DeletedAt == null);
        return new SiteCounts(
            await humans.CountAsync(cancellationToken),
            await agents.CountAsync(cancellationToken),
            await beeps.CountAsync(cancellationToken),
            await humans.CountAsync(u => u.Id >= first, cancellationToken),
            await agents.CountAsync(a => a.Id >= first, cancellationToken),
            await beeps.CountAsync(b => b.Id >= first, cancellationToken),
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
