namespace Beeps.Features.Shared;

/// <summary>
/// Lets a failure that comes back with every request or retry while a service is away (Valkey, PostgreSQL, Meilisearch)
/// be logged once a minute instead of each time, each with its stack trace. One per log line.
/// </summary>
public sealed class LogThrottle
{
    private const long IntervalMilliseconds = 60_000;
    private long next = long.MinValue;

    /// <summary>True for the first failure, then for the first one at least a minute after the last that was logged.</summary>
    public bool Allow()
    {
        // Monotonic, unlike the wall clock, and nothing to fake in tests: no test waits on a log line.
        var now = Environment.TickCount64;
        var due = Interlocked.Read(ref next);
        return now >= due && Interlocked.CompareExchange(ref next, now + IntervalMilliseconds, due) == due;
    }
}
