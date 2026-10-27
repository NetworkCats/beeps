namespace Beeps.Infrastructure;

/// <summary>
/// Gives the server back the memory the GC holds for nothing once the app is quiet. The GC only collects while the app
/// allocates, and keeps what it freed for the next burst: after a crawl through public pages, an idle instance held
/// 430 MB with 120 MB alive, which PostgreSQL could have had as page cache. Each minute, if the app allocated little
/// and the GC holds much more than after the last trim, one compacting collection returns the rest. It pauses the app
/// for tens of milliseconds, at a time when there is little to hold up.
/// </summary>
public sealed partial class MemoryTrimmer(TimeProvider time, ILogger<MemoryTrimmer> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly TrimPolicy policy = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        policy.Due(GC.GetTotalAllocatedBytes(), GC.GetGCMemoryInfo().TotalCommittedBytes);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!policy.Due(GC.GetTotalAllocatedBytes(), GC.GetGCMemoryInfo().TotalCommittedBytes))
                {
                    continue;
                }

                var before = Environment.WorkingSet;
                var started = time.GetTimestamp();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                var elapsed = time.GetElapsedTime(started);
                policy.Trimmed(GC.GetTotalAllocatedBytes(), GC.GetGCMemoryInfo().TotalCommittedBytes);
                LogTrimmed(logger, before >> 20, Environment.WorkingSet >> 20, (long)elapsed.TotalMilliseconds);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Quiet: returned the GC's spare memory, working set {Before} MB to {After} MB in {Elapsed} ms")]
    private static partial void LogTrimmed(ILogger logger, long before, long after, long elapsed);

    /// <summary>When a trim is due, from the bytes allocated so far and those the GC has committed, read once a minute.</summary>
    internal sealed class TrimPolicy
    {
        /// <summary>Allocated in a minute by an app with little to do: the workers alone allocate about ten megabytes.</summary>
        internal const long QuietBytes = 64L << 20;

        /// <summary>Committed past what the last trim left, worth a full collection to give back.</summary>
        internal const long SlackBytes = 64L << 20;

        private long allocated = -1;
        private long baseline;

        /// <summary>Whether to trim now; the first reading only sets where counting starts.</summary>
        public bool Due(long totalAllocated, long committed)
        {
            var quiet = allocated >= 0 && totalAllocated - allocated < QuietBytes;
            allocated = totalAllocated;
            if (baseline == 0)
            {
                baseline = committed;
            }

            return quiet && committed - baseline > SlackBytes;
        }

        /// <summary>After a trim: the GC's size now is what later growth is measured from.</summary>
        public void Trimmed(long totalAllocated, long committed)
        {
            allocated = totalAllocated;
            baseline = committed;
        }
    }
}
