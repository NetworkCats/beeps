using Microsoft.Extensions.Options;
using Npgsql;

namespace Beeps.Infrastructure.Outbox;

/// <summary>
/// Drains one queue of the outbox whenever a NOTIFY arrives (new messages, <see cref="WorkSignals"/>) or the poll
/// interval passes (retries becoming due). Every instance runs one per queue; SKIP LOCKED keeps them from taking the
/// same message.
/// </summary>
public sealed partial class OutboxWorker(
    string queue,
    IServiceScopeFactory scopes,
    NpgsqlDataSource dataSource,
    IOptions<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    private const string ListenSql = $"LISTEN {WorkSignals.OutboxChannel}";

    /// <summary>The queue it drains, one of <see cref="Features.Shared.OutboxQueues"/>.</summary>
    public string Queue => queue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            LogDisabled(logger, queue);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);
                await using (var listen = new NpgsqlCommand(ListenSql, connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    await DrainAsync(settings.BatchSize, stoppingToken);
                    // Notifications that arrive while draining are buffered on the connection and end this wait at once.
                    await connection.WaitAsync(settings.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogLoopFailed(logger, exception, queue);
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

    private async Task DrainAsync(int batchSize, CancellationToken cancellationToken)
    {
        int claimed;
        do
        {
            await using var scope = scopes.CreateAsyncScope();
            claimed = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(queue, cancellationToken);
        }
        while (claimed >= batchSize);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox worker for the {Queue} queue is disabled on this instance")]
    private static partial void LogDisabled(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox worker loop for the {Queue} queue failed; reconnecting")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception, string queue);
}
