namespace Beeps.Infrastructure.Jobs;

/// <summary>Locking and run bookkeeping for scheduled jobs, behind an interface so the runner's decisions are testable.</summary>
public interface IScheduledJobStore
{
    /// <summary>Takes the job's advisory lock without waiting; null if another instance is running it right now.</summary>
    Task<IScheduledJobLease?> TryLockAsync(string jobName, CancellationToken cancellationToken);
}

/// <summary>Holds the job's lock until disposed. The lock is tied to a database connection, so a crashed instance releases it.</summary>
public interface IScheduledJobLease : IAsyncDisposable
{
    Task<DateTimeOffset?> GetLastStartedAsync(CancellationToken cancellationToken);

    Task MarkStartedAsync(DateTimeOffset startedAt, CancellationToken cancellationToken);

    Task MarkFinishedAsync(DateTimeOffset finishedAt, string? error, CancellationToken cancellationToken);
}
