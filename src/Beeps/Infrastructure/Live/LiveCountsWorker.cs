using Beeps.Features.Live;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Caching;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Live;

/// <summary>
/// Every few seconds, publishes the like and reply counts that changed (PLAN 6.10), and evicts the cached public pages
/// that show those beeps, so signed-out visitors see the counts as soon as signed-in pages do. Every instance runs one;
/// each takes different beeps.
/// </summary>
public sealed partial class LiveCountsWorker(LiveUpdates updates, CacheInvalidator invalidator, IOptions<LiveOptions> options, TimeProvider time, ILogger<LiveCountsWorker> logger) : BackgroundService
{
    private readonly LogThrottle failures = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.Value.CountsInterval, time, stoppingToken);
                IReadOnlyList<Guid> taken;
                do
                {
                    taken = await updates.PublishCountsAsync(stoppingToken);
                    invalidator.Invalidate([.. taken.Select(CacheChange.BeepCounts)]);
                }
                while (taken.Count >= LiveUpdates.CountsBatchSize);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (failures.Allow())
                {
                    LogFailed(logger, exception);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing live counts failed; retrying (logged at most once a minute)")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
