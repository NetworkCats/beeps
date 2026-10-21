using System.Text.Json;
using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Infrastructure.Caching;

/// <summary>
/// Applies the invalidations every instance publishes (<see cref="CacheInvalidator"/>) to this instance's own
/// copies: content cache entries and output-cached pages. Pub/Sub keeps no backlog, so after a reconnect, when
/// messages may have been missed, it drops all of them.
/// </summary>
public sealed partial class CacheInvalidationListener(
    ISharedContentCache shared,
    ContentCache cache,
    IOutputCacheStore pages,
    TimeProvider time,
    ILogger<CacheInvalidationListener> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Connecting may take a while; the host does not wait for it.
        await Task.Yield();
        var attempts = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await shared.SubscribeAsync(message => _ = ApplyAsync(message, stoppingToken), () => _ = ForgetEverythingAsync(stoppingToken));
                // Anything published before now was missed, and requests may have been cached from the start: on time,
                // only while connecting, which is not worth a log line; late, while Valkey was away.
                await ForgetEverythingAsync(stoppingToken, logged: attempts > 0);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (attempts++ == 0)
                {
                    LogSubscribeFailed(logger, exception, RetryDelay);
                }
            }

            try
            {
                await Task.Delay(RetryDelay, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ApplyAsync(byte[] payload, CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize(payload, ContentCacheJsonContext.Default.CacheInvalidationMessage);
            if (message is not null)
            {
                await CacheInvalidator.ApplyLocallyAsync(cache, pages, message, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The entries expire on their own; the local ones within seconds.
            LogApplyFailed(logger, exception);
        }
    }

    private async Task ForgetEverythingAsync(CancellationToken cancellationToken, bool logged = true)
    {
        try
        {
            cache.ClearLocal();
            await pages.EvictByTagAsync(PageTags.All, cancellationToken);
            if (logged)
            {
                LogCleared(logger);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogApplyFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscribing to cache invalidations failed; retrying every {Delay}")]
    private static partial void LogSubscribeFailed(ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Applying a cache invalidation failed")]
    private static partial void LogApplyFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconnected to Valkey; dropped this instance's cached copies, as invalidations may have been missed")]
    private static partial void LogCleared(ILogger logger);
}
