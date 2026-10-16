using Beeps.Features.Shared;

namespace Beeps.Features.Beeps;

/// <summary>
/// Writes the live like and reply counts back to the database (PLAN 6.6). It recounts from the like and
/// reply rows instead of copying Valkey, so the columns end up exact even if a live count drifted, and
/// then puts the exact numbers back into Valkey.
/// </summary>
public sealed partial class BeepCounterFlushJob(IBeepCounterStore counters, IBeepStore beeps, ILogger<BeepCounterFlushJob> logger) : IScheduledJob
{
    public const string JobName = "beeps.counters";

    public const int BatchSize = 500;

    public string Name => JobName;

    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        IReadOnlyList<Guid> batch;
        do
        {
            batch = await counters.TakeDirtyAsync(BatchSize, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            try
            {
                var counts = await beeps.RecountAsync(batch, cancellationToken);
                await counters.SetAsync(counts, cancellationToken);
            }
            catch
            {
                // Taken from the set already: put them back, or their columns would stay stale.
                await counters.MarkDirtyAsync(batch, CancellationToken.None);
                throw;
            }

            total += batch.Count;
        }
        while (batch.Count >= BatchSize);

        if (total > 0)
        {
            LogFlushed(logger, total);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote back the counts of {Count} beeps")]
    private static partial void LogFlushed(ILogger logger, int count);
}
