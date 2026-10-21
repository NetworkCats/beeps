using Beeps.Features.Shared;
using Npgsql;

namespace Beeps.Infrastructure.Data.Stores;

/// <summary>
/// Idempotency keys in PostgreSQL, on their own connections (they wrap the request's own database work). Kept there
/// rather than in Valkey because they must hold when Valkey does not: a retry during an outage would otherwise write
/// twice, and agents send a key with every write (skill.md).
/// </summary>
public sealed class PostgresIdempotencyStore(NpgsqlDataSource dataSource, TimeProvider time) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> TryClaimAsync(string key, string fingerprint, TimeSpan claimDuration, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        // A new key, or one whose claim or response expired, is claimed in one statement; concurrent claims of the same
        // key wait for each other on the primary key, and only one of them gets the row back.
        await using (var claim = new NpgsqlCommand(
            """
            INSERT INTO idempotency_keys (key, fingerprint, expires_at) VALUES (@key, @fingerprint, @expires)
            ON CONFLICT (key) DO UPDATE
                SET fingerprint = EXCLUDED.fingerprint, status_code = NULL, content_type = NULL, location = NULL, body = NULL, expires_at = EXCLUDED.expires_at
                WHERE idempotency_keys.expires_at <= @now
            RETURNING 1
            """,
            connection))
        {
            claim.Parameters.AddWithValue("key", key);
            claim.Parameters.AddWithValue("fingerprint", fingerprint);
            claim.Parameters.AddWithValue("expires", now + claimDuration);
            claim.Parameters.AddWithValue("now", now);
            if (await claim.ExecuteScalarAsync(cancellationToken) is not null)
            {
                return null;
            }
        }

        // A separate statement, so it sees the claim that beat this one.
        await using var read = new NpgsqlCommand("SELECT fingerprint, status_code, content_type, location, body FROM idempotency_keys WHERE key = @key", connection);
        read.Parameters.AddWithValue("key", key);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            // Released between the two statements: as good as unclaimed, but a retry is the safe answer.
            return new IdempotencyRecord(fingerprint, null, null, null, null);
        }

        return new IdempotencyRecord(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    public async Task ExtendAsync(string key, TimeSpan claimDuration, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE idempotency_keys SET expires_at = @expires WHERE key = @key AND status_code IS NULL");
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("expires", time.GetUtcNow() + claimDuration);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteAsync(string key, IdempotencyRecord record, TimeSpan retention, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var command = dataSource.CreateCommand(
            """
            UPDATE idempotency_keys
            SET fingerprint = @fingerprint, status_code = @status, content_type = @type, location = @location, body = @body, expires_at = @expires
            WHERE key = @key
            """);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("fingerprint", record.Fingerprint);
        command.Parameters.AddWithValue("status", (object?)record.StatusCode ?? DBNull.Value);
        command.Parameters.AddWithValue("type", (object?)record.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue("location", (object?)record.Location ?? DBNull.Value);
        command.Parameters.AddWithValue("body", (object?)record.Body ?? DBNull.Value);
        command.Parameters.AddWithValue("expires", time.GetUtcNow() + retention);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM idempotency_keys WHERE key = @key AND status_code IS NULL");
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM idempotency_keys WHERE key IN (SELECT key FROM idempotency_keys WHERE expires_at < @now ORDER BY expires_at LIMIT @batch)");
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("batch", batchSize);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
