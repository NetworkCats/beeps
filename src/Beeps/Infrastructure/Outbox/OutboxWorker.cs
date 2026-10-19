using Microsoft.Extensions.Options;
using Npgsql;

namespace Beeps.Infrastructure.Outbox;

/// <summary>
/// Drains the outbox whenever a NOTIFY arrives (new messages) or the poll interval passes (retries
/// becoming due). Every instance runs one; SKIP LOCKED keeps them from taking the same message.
/// </summary>
public sealed partial class OutboxWorker(
    IServiceScopeFactory scopes,
    NpgsqlDataSource dataSource,
    IOptions<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    /// <summary>The channel the <c>outbox_notify</c> trigger signals (created in the Initial migration).</summary>
    private const string Channel = "beeps_outbox";

    private const string ListenSql = $"LISTEN {Channel}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
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
                LogLoopFailed(logger, exception);
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
            claimed = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(cancellationToken);
        }
        while (claimed >= batchSize);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox worker is disabled on this instance")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox worker loop failed; reconnecting")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception);
}
