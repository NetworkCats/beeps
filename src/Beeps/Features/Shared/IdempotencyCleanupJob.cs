namespace Beeps.Features.Shared;

/// <summary>Deletes idempotency keys whose claim or kept response expired (a day after the write), which would otherwise pile up.</summary>
public sealed partial class IdempotencyCleanupJob(IIdempotencyStore store, TimeProvider time, ILogger<IdempotencyCleanupJob> logger) : IScheduledJob
{
    public const string JobName = "idempotency.cleanup";

    /// <summary>Small batches keep each delete short.</summary>
    public const int BatchSize = 1000;

    public string Name => JobName;

    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var total = 0;
        int deleted;
        do
        {
            deleted = await store.DeleteExpiredAsync(now, BatchSize, cancellationToken);
            total += deleted;
        }
        while (deleted >= BatchSize);

        LogCleaned(logger, total);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency cleanup deleted {Count} expired keys")]
    private static partial void LogCleaned(ILogger logger, int count);
}
