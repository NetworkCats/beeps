using Npgsql;

namespace Beeps.Infrastructure.Queues;

/// <param name="Attempts">Includes the current attempt.</param>
public sealed record ClaimedBeepJob(Guid Id, Guid BeepId, int Attempts);

/// <summary>Queue operations used by a worker, behind an interface so the retry logic is testable.</summary>
public interface IBeepJobStore
{
    /// <summary>
    /// Takes up to <paramref name="limit"/> due jobs that no other worker holds, counts an attempt on each
    /// and hides them until <paramref name="leaseUntil"/>. Oldest first.
    /// </summary>
    Task<IReadOnlyList<ClaimedBeepJob>> ClaimAsync(int limit, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

    Task CompleteAsync(Guid id, CancellationToken cancellationToken);

    Task RescheduleAsync(Guid id, DateTimeOffset runAfter, string error, CancellationToken cancellationToken);
}

/// <summary>
/// One queue table of jobs on beeps (id, beep_id, attempts, run_after, last_error). Same scheme as the outbox:
/// claiming pushes run_after to the lease end, SKIP LOCKED lets instances share.
/// </summary>
public abstract class PostgresBeepJobStore(NpgsqlDataSource dataSource, string table) : IBeepJobStore
{
    private readonly string claimSql = $"""
        UPDATE {table} AS j
        SET attempts = j.attempts + 1, run_after = @lease_until
        FROM (
            SELECT id FROM {table}
            WHERE run_after <= @now
            ORDER BY run_after, id
            LIMIT @limit
            FOR UPDATE SKIP LOCKED
        ) AS due
        WHERE j.id = due.id
        RETURNING j.id, j.beep_id, j.attempts
        """;

    private readonly string completeSql = $"DELETE FROM {table} WHERE id = @id";

    private readonly string rescheduleSql = $"UPDATE {table} SET run_after = @run_after, last_error = @error WHERE id = @id";

    public async Task<IReadOnlyList<ClaimedBeepJob>> ClaimAsync(int limit, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(claimSql);
        command.Parameters.AddWithValue("lease_until", leaseUntil);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("limit", limit);

        var jobs = new List<ClaimedBeepJob>(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new ClaimedBeepJob(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2)));
        }

        // UUIDv7 ids sort by creation time.
        jobs.Sort((a, b) => a.Id.CompareTo(b.Id));
        return jobs;
    }

    public async Task CompleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(completeSql);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RescheduleAsync(Guid id, DateTimeOffset runAfter, string error, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(rescheduleSql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("run_after", runAfter);
        command.Parameters.AddWithValue("error", error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
