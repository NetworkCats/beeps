using Beeps.Features.Live;
using Beeps.Infrastructure.Live;
using Beeps.Tests.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Live;

public sealed class LiveEventHubTests : IAsyncDisposable
{
    private readonly StreamStore store = new();
    private readonly LiveOptions options = new() { PollInterval = TimeSpan.FromMilliseconds(5) };
    private readonly FixedTimeProvider time = new(TestData.Now);
    private readonly CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
    private readonly CancellationTokenSource stopping = new();
    private readonly IHostApplicationLifetime lifetime = Substitute.For<IHostApplicationLifetime>();
    private LiveEventHub? hub;

    [Fact]
    public async Task EverySubscriberGetsWhatIsAddedAfterTheHubStarted()
    {
        store.Add("old");
        var hub = await StartAsync();
        using var first = hub.Subscribe(null);
        using var second = hub.Subscribe(null);

        store.Add("a");
        store.Add("b");

        Assert.Equal(["a", "b"], await TakeAsync(first, 2));
        Assert.Equal(["a", "b"], await TakeAsync(second, 2));
    }

    [Fact]
    public async Task AnEventIsReadAsSoonAsItIsAnnouncedNotAtTheNextPoll()
    {
        options.PollInterval = TimeSpan.FromHours(1);
        var hub = await StartAsync();
        using var subscription = hub.Subscribe(null);

        store.Add("announced");

        Assert.Equal(["announced"], await TakeAsync(subscription, 1));
    }

    [Fact]
    public async Task AnUnannouncedEventIsStillReadAtTheNextPoll()
    {
        var hub = await StartAsync();
        using var subscription = hub.Subscribe(null);

        store.Add("unannounced", announce: false);

        Assert.Equal(["unannounced"], await TakeAsync(subscription, 1));
    }

    [Fact]
    public async Task AReconnectingSubscriberGetsWhatItMissedOnceThenLiveOnes()
    {
        var seen = store.Add("seen");
        store.Add("missed 1");
        var hub = await StartAsync();
        store.Add("missed 2");
        using var subscription = hub.Subscribe(LiveEventId.Parse(seen));
        var events = subscription.ReadAllAsync(stop.Token).GetAsyncEnumerator(stop.Token);

        var replayed = await NextAsync(events, 2);
        store.Add("live");

        Assert.Equal(["missed 1", "missed 2"], replayed);
        Assert.Equal(["live"], await NextAsync(events, 1));
        await events.DisposeAsync();
    }

    [Fact]
    public async Task APositionTooOldToReplayIsReportedAsAGap()
    {
        options.ReplayWindow = TimeSpan.FromMinutes(10);
        var hub = await StartAsync();
        using var subscription = hub.Subscribe(LiveEventId.Before(TestData.Now - TimeSpan.FromMinutes(11)));

        store.Add("after");

        Assert.Equal([LiveEventTypes.Gap, "after"], await TakeAsync(subscription, 2));
    }

    [Fact]
    public async Task AnOldPositionWithNothingAfterItIsNoGap()
    {
        options.ReplayWindow = TimeSpan.FromMinutes(10);
        var seen = store.Add("seen");
        var hub = await StartAsync();
        time.Now = TestData.Now + TimeSpan.FromMinutes(11);
        using var subscription = hub.Subscribe(LiveEventId.Parse(seen));
        var events = subscription.ReadAllAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        var first = events.MoveNextAsync().AsTask();

        // Once the replay looked at where the stream ends (the hub looked once on starting).
        while (store.LastIdReads < 2)
        {
            await Task.Delay(1, TestSupport.Ct);
        }

        store.Add("after");

        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct));
        Assert.Equal("after", events.Current.Type);
        await events.DisposeAsync();
    }

    [Fact]
    public async Task AGapSaysWhereTheReaderStandsOnceCaughtUp()
    {
        options.MaxReplay = 1;
        var seen = store.Add("seen");
        store.Add("1");
        var last = store.Add("2");
        var hub = await StartAsync();
        using var subscription = hub.Subscribe(LiveEventId.Parse(seen));
        var events = subscription.ReadAllAsync(stop.Token).GetAsyncEnumerator(stop.Token);

        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct));
        Assert.Equal(new LiveEvent(last, LiveEventTypes.Gap, ""), events.Current);
        await events.DisposeAsync();
    }

    [Fact]
    public async Task AStreamTrimmedPastThePositionIsAGap()
    {
        var seen = store.Add("seen");
        store.Add("trimmed before it was read");
        store.Add("kept");
        store.TrimFirst();
        store.TrimFirst();
        var hub = await StartAsync();
        using var lost = hub.Subscribe(LiveEventId.Parse(seen));
        using var fine = hub.Subscribe(LiveEventId.Parse(seen) with { Sequence = LiveEventId.Parse(seen).Sequence + 1 });

        Assert.Equal([LiveEventTypes.Gap], await TakeAsync(lost, 1));
        Assert.Equal(["kept"], await TakeAsync(fine, 1));
    }

    [Fact]
    public async Task StreamsEndWhenTheStreamWasTrimmedPastTheHubWhileItCouldNotRead()
    {
        var hub = await StartAsync();
        using var open = hub.Subscribe(null);
        var reading = CountUntilEndAsync(open);
        var reads = store.Reads;
        store.FailReads = true;
        while (store.Reads == reads)
        {
            await Task.Delay(5, TestSupport.Ct);
        }

        store.Add("never read");
        store.Add("kept");
        store.TrimFirst();
        store.FailReads = false;

        Assert.Equal(0, await reading.WaitAsync(TimeSpan.FromSeconds(10), TestSupport.Ct));
    }

    [Fact]
    public async Task StreamsStayOpenWhenNothingWasTrimmedWhileTheHubCouldNotRead()
    {
        var hub = await StartAsync();
        using var subscription = hub.Subscribe(null);
        var reads = store.Reads;
        store.FailReads = true;
        while (store.Reads == reads)
        {
            await Task.Delay(5, TestSupport.Ct);
        }

        store.Add("late");
        store.FailReads = false;

        Assert.Equal(["late"], await TakeAsync(subscription, 1));
    }

    [Fact]
    public async Task AReplayThatCannotBeReadIsAGapNotAFailedStream()
    {
        var seen = store.Add("seen");
        var hub = await StartAsync();
        store.FailTrimmedReads = true;
        using var subscription = hub.Subscribe(LiveEventId.Parse(seen));

        store.Add("after");

        Assert.Equal([LiveEventTypes.Gap, "after"], await TakeAsync(subscription, 2));
    }

    [Fact]
    public async Task TooMuchToReplayIsAGap()
    {
        options.MaxReplay = 2;
        var seen = store.Add("seen");
        store.Add("1");
        store.Add("2");
        store.Add("3");
        var hub = await StartAsync();
        using var subscription = hub.Subscribe(LiveEventId.Parse(seen));

        Assert.Equal([LiveEventTypes.Gap], await TakeAsync(subscription, 1));
    }

    [Fact]
    public async Task ASubscriberThatFallsTooFarBehindIsEnded()
    {
        var hub = await StartAsync();
        using var slow = hub.Subscribe(null);

        for (var i = 0; i <= LiveEventHub.SubscriberCapacity; i++)
        {
            store.Add($"{i}");
        }

        // An empty read after the last one was added: the hub reads one batch after another, handing each out before
        // the next read, so everything was handed out before the subscriber reads anything.
        var emptyReads = store.EmptyReads;
        while (store.EmptyReads == emptyReads)
        {
            await Task.Delay(5, TestSupport.Ct);
        }

        var received = await CountAsync(slow).WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct);

        Assert.Equal(LiveEventHub.SubscriberCapacity, received);
    }

    [Fact]
    public async Task StreamsEndWhenTheInstanceShutsDownSoTheirClientsReconnectElsewhere()
    {
        var hub = await StartAsync();
        using var open = hub.Subscribe(null);
        var reading = CountUntilEndAsync(open);

        await hub.StopAsync(TestSupport.Ct);
        using var late = hub.Subscribe(null);

        Assert.Equal(0, await reading.WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct));
        Assert.Equal(0, await CountUntilEndAsync(late).WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct));
    }

    [Fact]
    public async Task StreamsEndAsSoonAsTheInstanceStartsShuttingDownNotWhenTheServerGivesUpWaiting()
    {
        var hub = await StartAsync();
        using var open = hub.Subscribe(null);
        var reading = CountUntilEndAsync(open);

        // The server is still draining requests; the hub itself is not stopped yet.
        await stopping.CancelAsync();
        using var late = hub.Subscribe(null);

        Assert.Equal(0, await reading.WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct));
        Assert.Equal(0, await CountUntilEndAsync(late).WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct));
    }

    [Fact]
    public async Task WhenDisabledTheStreamIsNotPolledButReplaysStillFinish()
    {
        options.Enabled = false;
        store.Add("x");
        var hub = await StartAsync();
        using var subscription = hub.Subscribe(LiveEventId.Before(TestData.Now));

        Assert.Equal(["x"], await TakeAsync(subscription, 1));
        Assert.Equal(1, store.Reads);
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        if (hub is not null)
        {
            await hub.StopAsync(CancellationToken.None);
            hub.Dispose();
        }

        stop.Dispose();
        stopping.Dispose();
    }

    private async Task<LiveEventHub> StartAsync()
    {
        lifetime.ApplicationStopping.Returns(stopping.Token);
        hub = new LiveEventHub(store, Options.Create(options), time, lifetime, NullLogger<LiveEventHub>.Instance);
        await hub.StartAsync(stop.Token);
        // Once it has read the stream's end, anything added afterwards counts as new.
        while (options.Enabled && store.Reads == 0)
        {
            await Task.Delay(5, TestSupport.Ct);
        }

        return hub;
    }

    /// <summary>Reads with the test's own token, so only the stream ending stops it.</summary>
    private static async Task<int> CountUntilEndAsync(ILiveSubscription subscription)
    {
        var count = 0;
        await foreach (var _ in subscription.ReadAllAsync(TestSupport.Ct))
        {
            count++;
        }

        return count;
    }

    private async Task<int> CountAsync(ILiveSubscription subscription)
    {
        var count = 0;
        await foreach (var _ in subscription.ReadAllAsync(stop.Token).WithCancellation(stop.Token))
        {
            count++;
        }

        return count;
    }

    private async Task<List<string>> TakeAsync(ILiveSubscription subscription, int count)
    {
        var events = subscription.ReadAllAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        try
        {
            return await NextAsync(events, count);
        }
        finally
        {
            await events.DisposeAsync();
        }
    }

    private static async Task<List<string>> NextAsync(IAsyncEnumerator<LiveEvent> events, int count)
    {
        var types = new List<string>();
        while (types.Count < count)
        {
            Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct));
            types.Add(events.Current.Type);
        }

        return types;
    }

    /// <summary>The stream as a list; an event's type is its label, so tests can tell them apart.</summary>
    private sealed class StreamStore : ILiveEventStore
    {
        private readonly Lock gate = new();
        private readonly List<LiveEvent> events = [];
        private long sequence;
        private LiveEventId? trimmed;
        private Action? appended;

        public int Reads { get; private set; }

        public int EmptyReads { get; private set; }

        /// <param name="announce">Whether the hub hears of it, as over Pub/Sub; it may miss the word.</param>
        public string Add(string label, bool announce = true)
        {
            string id;
            lock (gate)
            {
                id = new LiveEventId(TestData.Now.ToUnixTimeMilliseconds(), ++sequence).ToString();
                events.Add(new LiveEvent(id, label, "{}"));
            }

            if (announce)
            {
                appended?.Invoke();
            }

            return id;
        }

        public void TrimFirst()
        {
            lock (gate)
            {
                trimmed = LiveEventId.Parse(events[0].Id);
                events.RemoveAt(0);
            }
        }

        public Task<IReadOnlyList<LiveEvent>> ReadAfterAsync(LiveEventId after, int max, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                Reads++;
                if (FailReads)
                {
                    throw new InvalidOperationException("Valkey is away.");
                }

                IReadOnlyList<LiveEvent> read = [.. events.Where(e => LiveEventId.Parse(e.Id) > after).Take(max)];
                EmptyReads += read.Count == 0 ? 1 : 0;
                return Task.FromResult(read);
            }
        }

        /// <summary>Valkey away for the hub: the stream cannot be read.</summary>
        public bool FailReads { get; set; }

        /// <summary>Valkey away, for a replay: where the stream was trimmed cannot be read.</summary>
        public bool FailTrimmedReads { get; set; }

        public Task<LiveEventId?> TrimmedUpToAsync(CancellationToken cancellationToken)
        {
            if (FailTrimmedReads)
            {
                throw new InvalidOperationException("Valkey is away.");
            }

            lock (gate)
            {
                return Task.FromResult(trimmed);
            }
        }

        public int LastIdReads { get; private set; }

        public Task<LiveEventId?> LastIdAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                LastIdReads++;
                return Task.FromResult<LiveEventId?>(events.Count == 0 ? null : LiveEventId.Parse(events[^1].Id));
            }
        }

        public Task SubscribeAppendedAsync(Action appended, CancellationToken cancellationToken)
        {
            this.appended = appended;
            return Task.CompletedTask;
        }

        public Task AppendAsync(string type, string data, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkCountsChangedAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> TakeChangedCountsAsync(int max, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> AddConnectionAsync(Guid actorId, string connectionId, DateTimeOffset now, int max, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveConnectionAsync(Guid actorId, string connectionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
