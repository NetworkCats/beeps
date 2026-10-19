using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Beeps.Features.Live;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Live;

/// <summary>
/// Reads the shared stream once for the whole instance and hands each event to every connection on it
/// (PLAN 6.10: events cross instances through Valkey). StackExchange.Redis cannot block on XREAD, so the hub
/// reads whenever Pub/Sub says an event was added, and polls in case such a word was lost; reading an empty stream
/// end is cheap. It reads even with no connection open, so a new connection can rely on it being caught up.
/// </summary>
public sealed partial class LiveEventHub(
    ILiveEventStore store,
    IOptions<LiveOptions> options,
    TimeProvider time,
    IHostApplicationLifetime lifetime,
    ILogger<LiveEventHub> logger)
    : BackgroundService, ILiveEventFeed
{
    /// <summary>Events a slow connection may have waiting before it is dropped (it reconnects and replays).</summary>
    public const int SubscriberCapacity = 1000;

    private const int BatchSize = 500;

    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(2);

    /// <summary>Holds one word that events were added since the last read; more words before that read change nothing.</summary>
    private readonly Channel<bool> appended = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private int subscribeFailures;

    private readonly Lock gate = new();
    private readonly List<Subscription> subscriptions = [];

    /// <summary>Set when the instance shuts down: nothing more will be delivered here.</summary>
    private bool stopped;

    /// <summary>Completed once the hub knows where the stream ends; replays wait for it, so they and live delivery meet without a gap.</summary>
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ILiveSubscription Subscribe(LiveEventId? after)
    {
        var subscription = new Subscription(this, after);
        lock (gate)
        {
            if (stopped)
            {
                // Shutting down: end at once, so the client reconnects to an instance that delivers.
                subscription.End();
            }
            else
            {
                subscriptions.Add(subscription);
            }
        }

        return subscription;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogDisabled(logger);
            ready.TrySetResult();
            return;
        }

        // Streams end as soon as the instance starts shutting down: the server would otherwise wait for them (up to its
        // shutdown timeout) with nothing more to deliver, while their clients could already be on another instance.
        using var stopping = lifetime.ApplicationStopping.Register(EndAll);
        using var ending = stoppingToken.Register(EndAll);
        // Connecting may take a while; the host does not wait for it.
        await Task.Yield();
        LiveEventId? position = null;
        var subscribed = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Before the first read, so nothing added after it goes unheard. Polling covers the time without it.
                subscribed = subscribed || await TrySubscribeAsync(stoppingToken);
                position ??= await store.LastIdAsync(stoppingToken) ?? LiveEventId.Zero;
                ready.TrySetResult();
                var events = await store.ReadAfterAsync(position.Value, BatchSize, stoppingToken);
                if (events.Count > 0)
                {
                    Dispatch(events);
                    position = LiveEventId.Parse(events[^1].Id);
                    if (events.Count == BatchSize)
                    {
                        continue;
                    }
                }

                await WaitForEventsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogReadFailed(logger, exception);
                try
                {
                    await Task.Delay(ErrorDelay, time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task<bool> TrySubscribeAsync(CancellationToken stoppingToken)
    {
        try
        {
            await store.SubscribeAppendedAsync(() => appended.Writer.TryWrite(true), stoppingToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Tried again before every read; said once.
            if (subscribeFailures++ == 0)
            {
                LogSubscribeFailed(logger, exception, options.Value.PollInterval);
            }

            return false;
        }
    }

    /// <summary>Until an instance says it added events, or for the poll interval, in case that word was lost.</summary>
    private async Task WaitForEventsAsync(CancellationToken stoppingToken)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        await Task.WhenAny(appended.Reader.WaitToReadAsync(waiting.Token).AsTask(), Task.Delay(options.Value.PollInterval, time, waiting.Token));
        await waiting.CancelAsync();
        stoppingToken.ThrowIfCancellationRequested();
        // Taken before the read it causes: a word that comes during that read wakes the next wait.
        appended.Reader.TryRead(out _);
    }

    private void Dispatch(IReadOnlyList<LiveEvent> events)
    {
        lock (gate)
        {
            for (var i = subscriptions.Count - 1; i >= 0; i--)
            {
                var subscription = subscriptions[i];
                if (!events.All(subscription.Offer))
                {
                    subscriptions.RemoveAt(i);
                    subscription.End();
                    LogOverflow(logger);
                }
            }
        }
    }

    private void EndAll()
    {
        lock (gate)
        {
            stopped = true;
            foreach (var subscription in subscriptions)
            {
                subscription.End();
            }

            subscriptions.Clear();
        }
    }

    private void Remove(Subscription subscription)
    {
        lock (gate)
        {
            subscriptions.Remove(subscription);
        }
    }

    /// <summary>Null when the events after <paramref name="start"/> cannot all be replayed: too old, too many, or trimmed away.</summary>
    private async Task<IReadOnlyList<LiveEvent>?> ReplayAsync(LiveEventId start, CancellationToken cancellationToken)
    {
        await ready.Task.WaitAsync(cancellationToken);
        var settings = options.Value;
        if (start.Time < time.GetUtcNow() - settings.ReplayWindow
            || await store.TrimmedUpToAsync(cancellationToken) is { } trimmed && trimmed > start)
        {
            return null;
        }

        var events = await store.ReadAfterAsync(start, settings.MaxReplay + 1, cancellationToken);
        return events.Count > settings.MaxReplay ? null : events;
    }

    /// <summary>
    /// One connection's view. It is registered before it replays, so anything added to the stream after the
    /// replay's read is also delivered live; events seen in both are skipped by id.
    /// </summary>
    private sealed class Subscription(LiveEventHub hub, LiveEventId? after) : ILiveSubscription
    {
        private readonly Channel<LiveEvent> channel = Channel.CreateBounded<LiveEvent>(
            new BoundedChannelOptions(SubscriberCapacity) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

        public bool Offer(LiveEvent item) => channel.Writer.TryWrite(item);

        /// <summary>Ends the connection's stream; the client reconnects and replays what it missed.</summary>
        public void End() => channel.Writer.TryComplete();

        public async IAsyncEnumerable<LiveEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var last = after;
            if (after is { } start)
            {
                var replayed = await hub.ReplayAsync(start, cancellationToken);
                if (replayed is null)
                {
                    yield return new LiveEvent("", LiveEventTypes.Gap, "");
                    last = null;
                }
                else
                {
                    foreach (var item in replayed)
                    {
                        yield return item;
                        last = LiveEventId.Parse(item.Id);
                    }
                }
            }

            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (last is { } seen && LiveEventId.Parse(item.Id) <= seen)
                {
                    continue;
                }

                yield return item;
            }
        }

        public void Dispose()
        {
            hub.Remove(this);
            channel.Writer.TryComplete();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Live events are not read on this instance")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscribing to new live events failed; reading them every {Interval} until it works")]
    private static partial void LogSubscribeFailed(ILogger logger, Exception exception, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading live events failed; retrying")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "A live connection fell too far behind and was ended")]
    private static partial void LogOverflow(ILogger logger);
}
