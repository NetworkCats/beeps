namespace Beeps.Features.Shared;

/// <summary>
/// Periodic work (cleanups and the like). Across all instances a job runs at most once per
/// <see cref="Interval"/>: the runner holds a PostgreSQL advisory lock and records the last run (PLAN 6.4).
/// Jobs must be idempotent; a crash mid-run means the next run does the rest.
/// </summary>
public interface IScheduledJob
{
    /// <summary>Stable name; it keys the lock and the recorded last run.</summary>
    string Name { get; }

    TimeSpan Interval { get; }

    Task RunAsync(CancellationToken cancellationToken);
}
