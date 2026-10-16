using Beeps.Features.Shared;
using Npgsql;

namespace Beeps.Infrastructure.Jobs;

public sealed class PostgresScheduledJobStore(NpgsqlDataSource dataSource) : IScheduledJobStore
{
    public async Task<IScheduledJobLease?> TryLockAsync(string jobName, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", AdvisoryLocks.ScheduledJob(jobName));
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new Lease(connection, jobName);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Lease(NpgsqlConnection connection, string jobName) : IScheduledJobLease
    {
        private const string LastStartedSql = "SELECT last_started_at FROM scheduled_jobs WHERE name = @name";

        private const string StartedSql = """
            INSERT INTO scheduled_jobs (name, last_started_at) VALUES (@name, @at)
            ON CONFLICT (name) DO UPDATE SET last_started_at = EXCLUDED.last_started_at
            """;

        private const string FinishedSql = "UPDATE scheduled_jobs SET last_finished_at = @at, last_error = @error WHERE name = @name";

        public async Task<DateTimeOffset?> GetLastStartedAsync(CancellationToken cancellationToken)
        {
            await using var command = Command(LastStartedSql);
            return await command.ExecuteScalarAsync(cancellationToken) is DateTime value
                ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
                : null;
        }

        public async Task MarkStartedAsync(DateTimeOffset startedAt, CancellationToken cancellationToken)
        {
            await using var command = Command(StartedSql);
            command.Parameters.AddWithValue("at", startedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task MarkFinishedAsync(DateTimeOffset finishedAt, string? error, CancellationToken cancellationToken)
        {
            await using var command = Command(FinishedSql);
            command.Parameters.AddWithValue("at", finishedAt);
            command.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", AdvisoryLocks.ScheduledJob(jobName));
                await command.ExecuteScalarAsync();
            }
            finally
            {
                // Closing the connection would release the lock anyway; unlocking first keeps pooled connections clean.
                await connection.DisposeAsync();
            }
        }

        private NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("name", jobName);
            return command;
        }
    }
}
