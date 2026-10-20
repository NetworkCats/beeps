using Beeps.Features.Shared;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Outbox;

/// <summary>Claims one batch of a queue and runs each message through its handler, with retry and dead-lettering.</summary>
public sealed partial class OutboxProcessor
{
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

    /// <param name="queue">One of <see cref="OutboxQueues"/>.</param>
    /// <returns>How many messages were claimed; fewer than the batch size means the queue is drained.</returns>
    public async Task<int> ProcessBatchAsync(string queue, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        // Whole milliseconds, which PostgreSQL keeps exactly: StartAttemptAsync asks for this very value back.
        var leasedUntil = WholeMilliseconds(now + options.LeaseDuration);
        var batch = await store.ClaimAsync(TypesOf(queue), options.BatchSize, now, leasedUntil, cancellationToken);
        foreach (var message in batch)
        {
            // Slow handlers (a stalled engine) used up the lease: another worker may have claimed the rest again, and
            // running them here too would run them twice. They are left to whoever claims them next.
            if (time.GetUtcNow() >= leasedUntil)
            {
                break;
            }

            // Counted as it starts, not when the batch was claimed: one that kills the process uses up only its own
            // attempts, and is dead-lettered alone rather than with the messages claimed beside it.
            // Leased afresh as it starts too (the batch's lease may be nearly out by now), and only while still held here.
            if (await store.StartAttemptAsync(message.Id, leasedUntil, time.GetUtcNow() + options.LeaseDuration, cancellationToken) is { } attempts)
            {
                await ProcessAsync(message with { Attempts = attempts }, cancellationToken);
            }
        }

        return batch.Count;
    }

    private static DateTimeOffset WholeMilliseconds(DateTimeOffset time) => DateTimeOffset.FromUnixTimeMilliseconds(time.ToUnixTimeMilliseconds());

    private OutboxTypes TypesOf(string queue) => queue == OutboxQueues.Main
        ? new([.. handlers.Values.Where(h => h.Queue != OutboxQueues.Main).Select(h => h.Type)], Except: true)
        : new([.. handlers.Values.Where(h => h.Queue == queue).Select(h => h.Type)], Except: false);

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
                await HandleLeasedAsync(handler, message, cancellationToken);
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
                error = FailedAttempts.Describe(exception);
            }
        }

        var failedAt = time.GetUtcNow();
        if (message.Attempts >= options.MaxAttempts)
        {
            await store.DeadLetterAsync(message.Id, error, failedAt, cancellationToken);
            LogDeadLettered(logger, message.Id, message.Type, message.Attempts);
        }
        else
        {
            await store.RescheduleAsync(message.Id, failedAt + FailedAttempts.RetryDelay(message.Attempts, options.RetryBaseDelay, options.RetryMaxDelay), error, cancellationToken);
        }
    }

    /// <summary>
    /// Runs the handler, keeping the message's lease while it does: one that takes longer (a search sync of a prolific
    /// account) would otherwise be claimed again and run beside itself, and in the end dead-lettered as it ran.
    /// </summary>
    private async Task HandleLeasedAsync(IOutboxHandler handler, OutboxEnvelope message, CancellationToken cancellationToken)
    {
        using var done = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewing = RenewLeaseAsync(message.Id, done.Token);
        try
        {
            await handler.HandleAsync(message, cancellationToken);
        }
        finally
        {
            await done.CancelAsync();
            await renewing;
        }
    }

    private async Task RenewLeaseAsync(Guid id, CancellationToken cancellationToken)
    {
        var every = options.LeaseDuration / 2;
        while (true)
        {
            try
            {
                await Task.Delay(every, time, cancellationToken);
                await store.ExtendLeaseAsync(id, time.GetUtcNow() + options.LeaseDuration, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Tried again next time round; at worst the message is claimed again, as without renewing.
                LogLeaseNotExtended(logger, exception, id);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The lease of outbox message {MessageId} could not be extended")]
    private static partial void LogLeaseNotExtended(ILogger logger, Exception exception, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({MessageType}) failed on attempt {Attempts}")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, Guid messageId, string messageType, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({MessageType}) moved to the dead-letter table after {Attempts} attempts")]
    private static partial void LogDeadLettered(ILogger logger, Guid messageId, string messageType, int attempts);
}
