using Beeps.Features.Shared;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Outbox;

/// <summary>Claims one batch and runs each message through its handler, with retry and dead-lettering.</summary>
public sealed partial class OutboxProcessor
{
    private const int MaxStoredErrorLength = 2000;

    private readonly IOutboxStore store;
    private readonly Dictionary<string, IOutboxHandler> handlers;
    private readonly OutboxOptions options;
    private readonly TimeProvider time;
    private readonly ILogger<OutboxProcessor> logger;

    public OutboxProcessor(
        IOutboxStore store,
        IEnumerable<IOutboxHandler> handlers,
        IOptions<OutboxOptions> options,
        TimeProvider time,
        ILogger<OutboxProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(options);
        this.store = store;
        this.options = options.Value;
        this.time = time;
        this.logger = logger;

        var byType = new Dictionary<string, IOutboxHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (!byType.TryAdd(handler.Type, handler))
            {
                throw new InvalidOperationException($"More than one outbox handler is registered for '{handler.Type}'.");
            }
        }

        this.handlers = byType;
    }

    /// <summary>Exponential backoff: base, 2×base, 4×base… capped at the maximum.</summary>
    public static TimeSpan RetryDelay(int attempts, OutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var exponent = Math.Clamp(attempts - 1, 0, 30);
        var delay = options.RetryBaseDelay * Math.Pow(2, exponent);
        return delay < options.RetryMaxDelay ? delay : options.RetryMaxDelay;
    }

    /// <returns>How many messages were claimed; fewer than the batch size means the queue is drained.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var batch = await store.ClaimAsync(options.BatchSize, now, now + options.LeaseDuration, cancellationToken);
        foreach (var message in batch)
        {
            await ProcessAsync(message, cancellationToken);
        }

        return batch.Count;
    }

    private async Task ProcessAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        string error;
        if (message.Attempts > options.MaxAttempts)
        {
            // Only reachable when earlier attempts never finished (the process died mid-handler and the
            // lease expired). Running it again could crash the next worker the same way.
            error = "Attempts exhausted without completing; the worker probably crashed while handling it.";
        }
        else if (!handlers.TryGetValue(message.Type, out var handler))
        {
            error = $"No handler is registered for message type '{message.Type}'.";
        }
        else
        {
            try
            {
                await handler.HandleAsync(message, cancellationToken);
                await store.CompleteAsync(message.Id, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutting down: leave it claimed; the lease expires and another worker retries it.
                throw;
            }
            catch (Exception exception)
            {
                LogHandlerFailed(logger, exception, message.Id, message.Type, message.Attempts);
                error = $"{exception.GetType().Name}: {exception.Message}";
            }
        }

        if (error.Length > MaxStoredErrorLength)
        {
            error = error[..MaxStoredErrorLength];
        }

        var failedAt = time.GetUtcNow();
        if (message.Attempts >= options.MaxAttempts)
        {
            await store.DeadLetterAsync(message.Id, error, failedAt, cancellationToken);
            LogDeadLettered(logger, message.Id, message.Type, message.Attempts);
        }
        else
        {
            await store.RescheduleAsync(message.Id, failedAt + RetryDelay(message.Attempts, options), error, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({MessageType}) failed on attempt {Attempts}")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, Guid messageId, string messageType, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({MessageType}) moved to the dead-letter table after {Attempts} attempts")]
    private static partial void LogDeadLettered(ILogger logger, Guid messageId, string messageType, int attempts);
}
