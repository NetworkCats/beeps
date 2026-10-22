using Beeps.Features.Shared;
using Beeps.Infrastructure.Outbox;
using Npgsql;

namespace Beeps.Infrastructure.Queues;

/// <summary>
/// Keeps up to <see cref="BeepQueueOptions.Concurrency"/> jobs of one queue running on this instance: a slot that
/// frees up takes the next due job at once, so one slow answer never holds up the others. Woken by NOTIFY when jobs
/// are queued (<see cref="WorkSignals"/>), and by the poll interval for retries that become due. Every instance runs
/// one per queue; SKIP LOCKED keeps them from taking the same job.
/// </summary>
public abstract partial class BeepJobWorker(
    IServiceScopeFactory scopes,
    IBeepJobStore store,
    NpgsqlDataSource dataSource,
    BeepQueueOptions settings,
    bool enabled,
    TimeProvider time,
    ILogger logger) : BackgroundService
{
    private readonly LogThrottle loopFailures = new();

    /// <summary>The queue, for the logs ("Moderation", "Translation").</summary>
    protected abstract string Queue { get; }

    /// <summary>The NOTIFY channel that wakes it (<see cref="WorkSignals"/>).</summary>
    protected abstract string Channel { get; }

    /// <summary>Runs one claimed job in a scope of its own; failures are the job's to record.</summary>
    protected abstract Task RunAsync(IServiceProvider services, ClaimedBeepJob job, CancellationToken stoppingToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!enabled || !settings.WorkerEnabled)
        {
            LogDisabled(logger, Queue);
            return;
        }

        // Outlives a lost connection: jobs already running carry on while the loop reconnects.
        var running = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunLoopAsync(running, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    if (loopFailures.Allow())
                    {
                        LogLoopFailed(logger, exception, Queue);
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
            // Each job handles its own failures; cancelled ones stay claimed until their lease ends.
            await Task.WhenAll(running);
        }
    }

    private async Task RunLoopAsync(List<Task> running, CancellationToken stoppingToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);
        await using (var listen = new NpgsqlCommand($"LISTEN {Channel}", connection))
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

    private async Task ProcessAsync(ClaimedBeepJob job, CancellationToken stoppingToken)
    {
        // Off the loop's thread at once, so a slow start of one job never delays claiming the next.
        await Task.Yield();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await RunAsync(scope.ServiceProvider, job, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // Recording the outcome failed (database away): the lease runs out and the job comes back.
            LogProcessFailed(logger, exception, Queue, job.BeepId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Queue} worker is disabled on this instance")]
    private static partial void LogDisabled(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Queue} worker loop failed; reconnecting (logged at most once a minute)")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Queue}: recording the job on beep {BeepId} failed; it is retried when its lease ends")]
    private static partial void LogProcessFailed(ILogger logger, Exception exception, string queue, Guid beepId);
}
