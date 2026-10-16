using Beeps.Features.Live;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Live;

/// <summary>Every few seconds, publishes the like and reply counts that changed (PLAN 6.10). Every instance runs one; each takes different beeps.</summary>
public sealed partial class LiveCountsWorker(LiveUpdates updates, IOptions<LiveOptions> options, TimeProvider time, ILogger<LiveCountsWorker> logger) : BackgroundService
{
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
                while (await updates.PublishCountsAsync(stoppingToken) >= LiveUpdates.CountsBatchSize)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing live counts failed; retrying")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
