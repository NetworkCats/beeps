using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Beeps.Features.Live;

namespace Beeps.Tests.Support;

/// <summary>A live feed the test drives: every open subscription receives what <see cref="Publish"/> sends.</summary>
public sealed class TestLiveFeed : ILiveEventFeed
{
    private readonly Lock gate = new();
    private readonly List<Channel<LiveEvent>> open = [];
    private int sequence;

    /// <summary>Where each subscription asked to start, in order.</summary>
    public List<LiveEventId?> Starts { get; } = [];

    /// <summary>Completes when a stream has subscribed, so a test publishes only once someone listens.</summary>
    public TaskCompletionSource Subscribed { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ILiveSubscription Subscribe(LiveEventId? after)
    {
        var channel = Channel.CreateUnbounded<LiveEvent>();
        lock (gate)
        {
            open.Add(channel);
            Starts.Add(after);
        }

        Subscribed.TrySetResult();
        return new Subscription(this, channel);
    }

    /// <summary>Sends an event with a fresh stream id to every open subscription; returns the id.</summary>
    public string Publish(string type, string data)
    {
        var id = $"1760603400000-{Interlocked.Increment(ref sequence)}";
        lock (gate)
        {
            foreach (var channel in open)
            {
                channel.Writer.TryWrite(new LiveEvent(id, type, data));
            }
        }

        return id;
    }

    /// <summary>Ends every open subscription, as the hub does with one that fell behind.</summary>
    public void EndAll()
    {
        lock (gate)
        {
            foreach (var channel in open)
            {
                channel.Writer.TryComplete();
            }
        }
    }

    private sealed class Subscription(TestLiveFeed feed, Channel<LiveEvent> channel) : ILiveSubscription
    {
        public async IAsyncEnumerable<LiveEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }

        public void Dispose()
        {
            lock (feed.gate)
            {
                feed.open.Remove(channel);
            }
        }
    }
}
