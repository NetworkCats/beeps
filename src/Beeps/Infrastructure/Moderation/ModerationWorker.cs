using Beeps.Features.Moderation;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Outbox;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Beeps.Infrastructure.Moderation;

/// <summary>
/// Keeps up to <see cref="ModerationOptions.Concurrency"/> reviews running on this instance: a slot
/// that frees up takes the next due review at once, so one slow answer never holds up the others. Woken by
/// NOTIFY when beeps are posted (<see cref="WorkSignals"/>), and by the poll interval for retries that become due.
/// Every instance runs one; SKIP LOCKED keeps them from taking the same review.
/// </summary>
public sealed partial class ModerationWorker(
    IServiceScopeFactory scopes,
    IModerationJobStore store,
    NpgsqlDataSource dataSource,
    IOptions<ModerationOptions> options,
    TimeProvider time,
    ILogger<ModerationWorker> logger) : BackgroundService
{
    private const string ListenSql = $"LISTEN {WorkSignals.ModerationChannel}";

    private readonly LogThrottle loopFailures = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.WorkerEnabled)
        {
            LogDisabled(logger);
            return;
        }

        // Outlives a lost connection: reviews already running carry on while the loop reconnects.
        var running = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunAsync(running, settings, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    if (loopFailures.Allow())
                    {
                        LogLoopFailed(logger, exception);
                    }
                    try
                    {
                        await Task.Delay(settings.ErrorDelay, time, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            // Each review handles its own failures; cancelled ones stay claimed until their lease ends.
            await Task.WhenAll(running);
        }
    }

    private async Task RunAsync(List<Task> running, ModerationOptions settings, CancellationToken stoppingToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);
        await using (var listen = new NpgsqlCommand(ListenSql, connection))
        {
            await listen.ExecuteNonQueryAsync(stoppingToken);
        }

        Task<bool>? notified = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            running.RemoveAll(t => t.IsCompleted);
            var free = settings.Concurrency - running.Count;
            if (free > 0)
            {
                var now = time.GetUtcNow();
                var claimed = await store.ClaimAsync(free, now, now + settings.LeaseDuration, stoppingToken);
                running.AddRange(claimed.Select(job => ProcessAsync(job, stoppingToken)));
                if (claimed.Count == free)
                {
                    // Every slot took one: there may be more waiting, as soon as a slot frees up.
                    await Task.WhenAny(running);
                    continue;
                }
            }

            // Notifications that arrive meanwhile are buffered on the connection and end this wait at once. The
            // same wait is kept across iterations: a connection serves one wait at a time.
            notified ??= connection.WaitAsync(settings.PollInterval, stoppingToken);
            await Task.WhenAny(running.Append<Task>(notified));
            if (notified.IsCompleted)
            {
                await notified;
                notified = null;
            }
        }
    }

    private async Task ProcessAsync(ClaimedModerationJob job, CancellationToken stoppingToken)
    {
        // Off the loop's thread at once, so a slow start of one review never delays claiming the next.
        await Task.Yield();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ModerationProcessor>().ProcessAsync(job, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // Recording the outcome failed (database away): the lease runs out and the review comes back.
            LogProcessFailed(logger, exception, job.BeepId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Moderation worker is disabled on this instance")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Moderation worker loop failed; reconnecting (logged at most once a minute)")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording the review of beep {BeepId} failed; it is retried when its lease ends")]
    private static partial void LogProcessFailed(ILogger logger, Exception exception, Guid beepId);
}
