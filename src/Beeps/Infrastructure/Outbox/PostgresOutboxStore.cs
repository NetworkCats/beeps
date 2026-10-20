using Beeps.Features.Shared;
using Npgsql;

namespace Beeps.Infrastructure.Outbox;

public sealed class PostgresOutboxStore(NpgsqlDataSource dataSource) : IOutboxStore
{
    // Claiming pushes run_after to the lease end, so the row needs no separate lock column and a
    // crashed worker's messages simply become due again. SKIP LOCKED lets instances claim in parallel.
    // Attempts are counted one message at a time, as each starts (StartAttemptSql).
    private const string ClaimSql = """
        UPDATE outbox AS o
        SET run_after = @lease_until
        FROM (
            SELECT id FROM outbox
            WHERE run_after <= @now AND (type = ANY(@types)) <> @except
            ORDER BY run_after, id
            LIMIT @limit
            FOR UPDATE SKIP LOCKED
        ) AS due
        WHERE o.id = due.id
        RETURNING o.id, o.type, o.payload::text, o.attempts, o.created_at
        """;

    private const string StartAttemptSql = """
        UPDATE outbox SET attempts = attempts + 1, run_after = @lease_until
        WHERE id = @id AND run_after = @claimed_until
        RETURNING attempts
        """;

    private const string ExtendLeaseSql = "UPDATE outbox SET run_after = @lease_until WHERE id = @id";

    private const string CompleteSql = "DELETE FROM outbox WHERE id = @id";

    private const string RescheduleSql = "UPDATE outbox SET run_after = @run_after, last_error = @error WHERE id = @id";

    private const string DeadLetterSql = """
        WITH moved AS (
            DELETE FROM outbox WHERE id = @id
            RETURNING id, type, payload, attempts, created_at
        )
        INSERT INTO outbox_dead (id, type, payload, attempts, last_error, created_at, failed_at)
        SELECT id, type, payload, attempts, @error, created_at, @failed_at FROM moved
        """;

    public async Task<IReadOnlyList<OutboxEnvelope>> ClaimAsync(OutboxTypes types, int limit, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(types);
        await using var command = dataSource.CreateCommand(ClaimSql);
        command.Parameters.AddWithValue("types", types.Types.ToArray());
        command.Parameters.AddWithValue("except", types.Except);
        command.Parameters.AddWithValue("lease_until", leaseUntil);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("limit", limit);

        var messages = new List<OutboxEnvelope>(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new OutboxEnvelope(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }

        // UUIDv7 ids sort by creation time.
        messages.Sort((a, b) => a.Id.CompareTo(b.Id));
        return messages;
    }

    public async Task<int?> StartAttemptAsync(Guid id, DateTimeOffset claimedUntil, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(StartAttemptSql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("claimed_until", claimedUntil);
        command.Parameters.AddWithValue("lease_until", leaseUntil);
        return await command.ExecuteScalarAsync(cancellationToken) is int attempts ? attempts : null;
    }

    public async Task ExtendLeaseAsync(Guid id, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(ExtendLeaseSql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("lease_until", leaseUntil);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    public async Task DeadLetterAsync(Guid id, string error, DateTimeOffset failedAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(DeadLetterSql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("error", error);
        command.Parameters.AddWithValue("failed_at", failedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
