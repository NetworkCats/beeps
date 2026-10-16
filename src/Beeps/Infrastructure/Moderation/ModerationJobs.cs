using Beeps.Features.Moderation;
using Beeps.Infrastructure.Data;
using Npgsql;

namespace Beeps.Infrastructure.Moderation;

/// <summary>
/// Stages reviews on the request's DbContext, so the next <c>SaveChangesAsync</c> commits them with the beep. A
/// trigger on the table NOTIFYs the workers on commit.
/// </summary>
public sealed class EfModerationJobs(BeepsDbContext db, TimeProvider time) : IModerationJobs
{
    public void Enqueue(Guid beepId)
    {
        var now = time.GetUtcNow();
        db.Set<ModerationJob>().Add(new ModerationJob { Id = Guid.CreateVersion7(now), BeepId = beepId, RunAfter = now, CreatedAt = now });
    }
}

/// <param name="Attempts">Includes the current attempt.</param>
public sealed record ClaimedModerationJob(Guid Id, Guid BeepId, int Attempts);

/// <summary>Queue operations used by the worker, behind an interface so the retry logic is testable.</summary>
public interface IModerationJobStore
{
    /// <summary>
    /// Takes up to <paramref name="limit"/> due reviews that no other worker holds, counts an attempt on each
    /// and hides them until <paramref name="leaseUntil"/>. Oldest first.
    /// </summary>
    Task<IReadOnlyList<ClaimedModerationJob>> ClaimAsync(int limit, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

    Task CompleteAsync(Guid id, CancellationToken cancellationToken);

    Task RescheduleAsync(Guid id, DateTimeOffset runAfter, string error, CancellationToken cancellationToken);
}

public sealed class PostgresModerationJobStore(NpgsqlDataSource dataSource) : IModerationJobStore
{
    // Same scheme as the outbox: claiming pushes run_after to the lease end, SKIP LOCKED lets instances share.
    private const string ClaimSql = """
        UPDATE moderation_jobs AS j
        SET attempts = j.attempts + 1, run_after = @lease_until
        FROM (
            SELECT id FROM moderation_jobs
            WHERE run_after <= @now
            ORDER BY run_after, id
            LIMIT @limit
            FOR UPDATE SKIP LOCKED
        ) AS due
        WHERE j.id = due.id
        RETURNING j.id, j.beep_id, j.attempts
        """;

    private const string CompleteSql = "DELETE FROM moderation_jobs WHERE id = @id";

    private const string RescheduleSql = "UPDATE moderation_jobs SET run_after = @run_after, last_error = @error WHERE id = @id";

    public async Task<IReadOnlyList<ClaimedModerationJob>> ClaimAsync(int limit, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(ClaimSql);
        command.Parameters.AddWithValue("lease_until", leaseUntil);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("limit", limit);

        var jobs = new List<ClaimedModerationJob>(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new ClaimedModerationJob(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2)));
        }

        // UUIDv7 ids sort by creation time.
        jobs.Sort((a, b) => a.Id.CompareTo(b.Id));
        return jobs;
    }

    public async Task CompleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(CompleteSql);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RescheduleAsync(Guid id, DateTimeOffset runAfter, string error, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(RescheduleSql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("run_after", runAfter);
        command.Parameters.AddWithValue("error", error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
