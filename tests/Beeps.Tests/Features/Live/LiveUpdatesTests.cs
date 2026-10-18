using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Tests.Support;
using StackExchange.Redis;

namespace Beeps.Tests.Features.Live;

public sealed class LiveUpdatesTests
{
    private readonly ILiveEventStore store = Substitute.For<ILiveEventStore>();
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();

    [Fact]
    public async Task EventsAreAppendedAsSnakeCaseJson()
    {
        var beep = Guid.NewGuid();
        var agent = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var live = TestData.Live(store, counters);

        await live.BeepRemovedAsync(beep, TestSupport.Ct);
        await live.AgentConnectedAsync(agent, owner, TestSupport.Ct);
        await live.InboxChangedAsync([owner], TestSupport.Ct);
        await live.ActorChangedAsync(owner, TestSupport.Ct);
        await live.CloseConnectionsAsync(["a"], TestSupport.Ct);
        await live.BeepsChangedAsync([beep], TestSupport.Ct);
        await live.LikeChangedAsync(beep, owner, liked: true, TestSupport.Ct);
        await live.RelationChangedAsync(owner, agent, TestSupport.Ct);
        await live.AgentsChangedAsync(owner, TestSupport.Ct);
        await live.ResyncAsync(TestSupport.Ct);

        Assert.Equal([$$"""{"id":"{{beep:D}}"}"""], TestData.LiveEvents(store, LiveEventTypes.BeepRemoved));
        Assert.Equal([$$"""{"agent_id":"{{agent:D}}","owner_id":"{{owner:D}}"}"""], TestData.LiveEvents(store, LiveEventTypes.AgentConnected));
        Assert.Equal([$$"""{"recipient_ids":["{{owner:D}}"]}"""], TestData.LiveEvents(store, LiveEventTypes.Inbox));
        Assert.Equal([$$"""{"actor_id":"{{owner:D}}"}"""], TestData.LiveEvents(store, LiveEventTypes.ActorChanged));
        Assert.Equal(["""{"connection_ids":["a"]}"""], TestData.LiveEvents(store, LiveEventTypes.Close));
        Assert.Equal([$$"""{"ids":["{{beep:D}}"]}"""], TestData.LiveEvents(store, LiveEventTypes.BeepsChanged));
        Assert.Equal([$$"""{"beep_id":"{{beep:D}}","actor_id":"{{owner:D}}","liked":true}"""], TestData.LiveEvents(store, LiveEventTypes.LikeChanged));
        Assert.Equal([$$"""{"actor_ids":["{{owner:D}}","{{agent:D}}"]}"""], TestData.LiveEvents(store, LiveEventTypes.RelationChanged));
        Assert.Equal([$$"""{"owner_id":"{{owner:D}}"}"""], TestData.LiveEvents(store, LiveEventTypes.AgentsChanged));
        Assert.Equal(["{}"], TestData.LiveEvents(store, LiveEventTypes.Resync));
    }

    [Fact]
    public async Task NothingToSayPublishesNothing()
    {
        var live = TestData.Live(store, counters);

        await live.InboxChangedAsync([], TestSupport.Ct);
        await live.CloseConnectionsAsync([], TestSupport.Ct);
        await live.BeepsChangedAsync([], TestSupport.Ct);

        await store.DidNotReceiveWithAnyArgs().AppendAsync(default!, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task AValkeyOutageNeverFailsTheChangeThatPublishes()
    {
        store.AppendAsync(default!, default!, TestSupport.Ct).ReturnsForAnyArgs(Task.FromException(new RedisException("Valkey is down")));
        store.MarkCountsChangedAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(Task.FromException(new RedisException("Valkey is down")));
        var live = TestData.Live(store, counters);

        await live.BeepPostedAsync(new BeepPostedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(), []), TestSupport.Ct);
        await live.CountsChangedAsync(Guid.NewGuid(), TestSupport.Ct);
    }

    [Fact]
    public async Task ChangedCountsGoOutTogetherWithTheirCurrentValues()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        store.TakeChangedCountsAsync(LiveUpdates.CountsBatchSize, Arg.Any<CancellationToken>()).Returns([a, b]);
        // b's live counts expired meanwhile; a drifted below zero once.
        counters.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, BeepCounts> { [a] = new(-1, 2) });
        var live = TestData.Live(store, counters);

        await live.CountsChangedAsync(a, TestSupport.Ct);
        var taken = await live.PublishCountsAsync(TestSupport.Ct);

        Assert.Equal([a, b], taken);
        await store.Received(1).MarkCountsChangedAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == a), Arg.Any<CancellationToken>());
        Assert.Equal([$$"""{"beeps":[{"id":"{{a:D}}","like_count":0,"reply_count":2}]}"""], TestData.LiveEvents(store, LiveEventTypes.Counts));
    }

    [Fact]
    public async Task NoChangedCountsNoEvent()
    {
        store.TakeChangedCountsAsync(default, TestSupport.Ct).ReturnsForAnyArgs([]);

        Assert.Empty(await TestData.Live(store, counters).PublishCountsAsync(TestSupport.Ct));
        await counters.DidNotReceiveWithAnyArgs().GetAsync(default!, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().AppendAsync(default!, default!, TestSupport.Ct);
    }
}
