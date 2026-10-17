using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Features.Live;

public sealed class LiveServiceTests : IAsyncDisposable
{
    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Viewer TomViewer = Viewer.Human(Tom);

    private readonly TestLiveFeed feed = new();
    private readonly ILiveEventStore store = Substitute.For<ILiveEventStore>();
    private readonly IProfileStore profiles = Substitute.For<IProfileStore>();
    private readonly INotificationStore notifications = Substitute.For<INotificationStore>();
    private readonly LiveOptions options = new() { HeartbeatInterval = TimeSpan.FromHours(1) };
    private readonly FixedTimeProvider time = new(TestData.Now);
    private readonly CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
    private IAsyncEnumerator<LiveMessage>? stream;
    private int unread;

    public LiveServiceTests()
    {
        store.AddConnectionAsync(default, default!, default, default, TestSupport.Ct).ReturnsForAnyArgs([]);
        profiles.ListFollowedAsync(Tom, Arg.Any<CancellationToken>()).Returns([Alice]);
        notifications.CountUnreadAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs(_ => unread);
    }

    [Theory]
    [InlineData("nope", null, null, LiveErrorCodes.InvalidWatch)]
    [InlineData("home", "not-a-guid", null, LiveErrorCodes.InvalidBeeps)]
    [InlineData("home", null, "yesterday", LiveErrorCodes.InvalidEventId)]
    public async Task BadSubscriptionsAreRejected(string watch, string? beeps, string? lastEventId, string code)
    {
        var result = await CreateService().OpenAsync(TomViewer, new LiveRequest(watch, beeps, lastEventId, null), TestSupport.Ct);

        Assert.Equal(code, result.Error?.Code);
        Assert.Equal(ErrorKind.Validation, result.Error?.Kind);
    }

    [Fact]
    public async Task APageMayListOnlySoManyBeeps()
    {
        var beeps = string.Join(',', Enumerable.Range(0, LiveService.MaxWatchedBeeps + 1).Select(_ => Guid.NewGuid()));

        var result = await CreateService().OpenAsync(TomViewer, new LiveRequest("", beeps, null, null), TestSupport.Ct);

        Assert.Equal(LiveErrorCodes.InvalidBeeps, result.Error?.Code);
    }

    [Fact]
    public async Task AConnectionStartsWithTheUnreadCountAndIsCountedUntilItEnds()
    {
        unread = 3;

        var first = await OpenAsync(new LiveRequest("home", null, null, null));
        var connectionId = (string)store.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ILiveEventStore.AddConnectionAsync)).GetArguments()[1]!;
        await stream!.DisposeAsync();
        stream = null;

        Assert.Equal(new LiveMessage(LiveMessages.Inbox, """{"unread_count":3}""", null, options.ReconnectDelay), first);
        await store.Received(1).AddConnectionAsync(Tom, connectionId, TestData.Now, options.MaxConnectionsPerAccount, Arg.Any<CancellationToken>());
        await store.Received(1).RemoveConnectionAsync(Tom, connectionId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReconnectionReplaysFromItsLastEventAndAFreshPageFromWhenItWasRendered()
    {
        await OpenAsync(new LiveRequest("", null, "1760603400000-7", TestData.Now.ToUnixTimeMilliseconds()));
        await stream!.DisposeAsync();
        await OpenAsync(new LiveRequest("", null, null, TestData.Now.ToUnixTimeMilliseconds()));

        Assert.Equal(LiveEventId.Parse("1760603400000-7"), feed.Starts[0]);
        // A little earlier than rendered: the app's and Valkey's clocks may disagree slightly.
        Assert.Equal(LiveEventId.Before(TestData.Now - TimeSpan.FromSeconds(2)), feed.Starts[1]);
    }

    [Fact]
    public async Task TheHomeTimelineHearsOfBeepsByTheFollowedAndThenFollowsThem()
    {
        await OpenAsync(new LiveRequest("home", null, null, null));
        var stranger = Posted(Guid.NewGuid());
        var followed = Posted(Alice);

        feed.Publish(LiveEventTypes.BeepPosted, Json(stranger));
        var id = feed.Publish(LiveEventTypes.BeepPosted, Json(followed));
        var beep = await NextAsync();
        feed.Publish(LiveEventTypes.Counts, $$"""{"beeps":[{"id":"{{stranger.Id:D}}","like_count":1,"reply_count":0},{"id":"{{followed.Id:D}}","like_count":2,"reply_count":1}]}""");
        var counts = await NextAsync();
        feed.Publish(LiveEventTypes.BeepRemoved, $$"""{"id":"{{stranger.Id:D}}"}""");
        feed.Publish(LiveEventTypes.BeepRemoved, $$"""{"id":"{{followed.Id:D}}"}""");
        var removed = await NextAsync();

        Assert.Equal(new LiveMessage(LiveMessages.Beep, $$"""{"id":"{{followed.Id:D}}","root_id":"{{followed.Id:D}}","reply_to_id":null}""", id), beep);
        Assert.Equal($$"""[{"id":"{{followed.Id:D}}","like_count":2,"reply_count":1}]""", counts.Data);
        Assert.Equal((LiveMessages.Removed, $$"""{"id":"{{followed.Id:D}}"}"""), (removed.Type, removed.Data));
    }

    [Fact]
    public async Task CountsAndRemovalConcernOnlyTheBeepsOnThePage()
    {
        var shown = Guid.NewGuid();
        await OpenAsync(new LiveRequest("", $"{shown},{shown}", null, null));

        feed.Publish(LiveEventTypes.BeepPosted, Json(Posted(Alice)));
        feed.Publish(LiveEventTypes.Counts, $$"""{"beeps":[{"id":"{{Guid.NewGuid():D}}","like_count":1,"reply_count":0}]}""");
        feed.Publish(LiveEventTypes.BeepRemoved, $$"""{"id":"{{shown:D}}"}""");

        Assert.Equal(LiveMessages.Removed, (await NextAsync()).Type);
    }

    [Fact]
    public async Task AHiddenBeepIsRemovedForEveryoneButItsAuthorAndTheirOwner()
    {
        var (own, agents, others) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await OpenAsync(new LiveRequest("", $"{own},{agents},{others}", null, null));

        feed.Publish(LiveEventTypes.BeepHidden, $$"""{"id":"{{own:D}}","author_id":"{{Tom:D}}","author_owner_id":null}""");
        feed.Publish(LiveEventTypes.BeepHidden, $$"""{"id":"{{agents:D}}","author_id":"{{Guid.NewGuid():D}}","author_owner_id":"{{Tom:D}}"}""");
        feed.Publish(LiveEventTypes.BeepHidden, $$"""{"id":"{{others:D}}","author_id":"{{Alice:D}}","author_owner_id":null}""");

        var removed = await NextAsync();
        Assert.Equal((LiveMessages.Removed, $$"""{"id":"{{others:D}}"}"""), (removed.Type, removed.Data));
    }

    [Fact]
    public async Task TheUnreadCountIsSentAgainWhenTheViewersInboxChanges()
    {
        await OpenAsync(new LiveRequest("", null, null, null));

        feed.Publish(LiveEventTypes.Inbox, $$"""{"recipient_ids":["{{Alice:D}}"]}""");
        unread = 5;
        var id = feed.Publish(LiveEventTypes.Inbox, $$"""{"recipient_ids":["{{Alice:D}}","{{Tom:D}}"]}""");

        Assert.Equal(new LiveMessage(LiveMessages.Inbox, """{"unread_count":5}""", id), await NextAsync());
    }

    [Fact]
    public async Task OwnersHearTheirAgentsConnectAndProfilePagesHearTheirProfileChange()
    {
        var agent = Guid.NewGuid();
        await OpenAsync(new LiveRequest($"profile:{Alice:D}", null, null, null));

        feed.Publish(LiveEventTypes.AgentConnected, $$"""{"agent_id":"{{Guid.NewGuid():D}}","owner_id":"{{Alice:D}}"}""");
        feed.Publish(LiveEventTypes.AgentConnected, $$"""{"agent_id":"{{agent:D}}","owner_id":"{{Tom:D}}"}""");
        var connected = await NextAsync();
        feed.Publish(LiveEventTypes.ActorChanged, $$"""{"actor_id":"{{Tom:D}}"}""");
        feed.Publish(LiveEventTypes.ActorChanged, $$"""{"actor_id":"{{Alice:D}}"}""");
        var changed = await NextAsync();

        Assert.Equal((LiveMessages.AgentConnected, $$"""{"id":"{{agent:D}}"}"""), (connected.Type, connected.Data));
        Assert.Equal((LiveMessages.Actor, $$"""{"id":"{{Alice:D}}"}"""), (changed.Type, changed.Data));
    }

    [Fact]
    public async Task OpeningOneTooManyClosesTheOldestAndAClosedConnectionEnds()
    {
        store.AddConnectionAsync(default, default!, default, default, TestSupport.Ct).ReturnsForAnyArgs(["oldest"]);
        await OpenAsync(new LiveRequest("", null, null, null));
        var mine = (string)store.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ILiveEventStore.AddConnectionAsync)).GetArguments()[1]!;

        feed.Publish(LiveEventTypes.Close, """{"connection_ids":["someone-else"]}""");
        feed.Publish(LiveEventTypes.Close, $$"""{"connection_ids":["{{mine}}"]}""");

        Assert.Equal(["""{"connection_ids":["oldest"]}"""], TestData.LiveEvents(store, LiveEventTypes.Close));
        Assert.Equal(LiveMessages.Close, (await NextAsync()).Type);
        Assert.False(await stream!.MoveNextAsync());
    }

    [Fact]
    public async Task MissedEventsMakeThePageCatchUp()
    {
        await OpenAsync(new LiveRequest("home", null, "1760603400000-1", null));

        feed.Publish(LiveEventTypes.Gap, "");

        Assert.Equal(new LiveMessage(LiveMessages.Reset, "{}"), await NextAsync());
    }

    [Fact]
    public async Task AnAgentsStreamCarriesOnlyItsInbox()
    {
        var agent = Viewer.Agent(Guid.NewGuid(), Tom);
        unread = 1;
        stream = CreateService().OpenInbox(agent, null, stop.Token).Value.GetAsyncEnumerator(stop.Token);
        var first = await NextAsync();

        feed.Publish(LiveEventTypes.BeepPosted, Json(Posted(Tom)));
        feed.Publish(LiveEventTypes.AgentConnected, $$"""{"agent_id":"{{agent.ActorId:D}}","owner_id":"{{Tom:D}}"}""");
        unread = 2;
        feed.Publish(LiveEventTypes.Inbox, $$"""{"recipient_ids":["{{agent.ActorId:D}}"]}""");
        var second = await NextAsync();
        feed.Publish(LiveEventTypes.Gap, "");
        var afterGap = await NextAsync();

        Assert.Equal("""{"unread_count":1}""", first.Data);
        Assert.Equal((LiveMessages.Inbox, """{"unread_count":2}"""), (second.Type, second.Data));
        Assert.Equal(LiveMessages.Inbox, afterGap.Type);
        await store.Received(1).AddConnectionAsync(agent.ActorId, Arg.Any<string>(), TestData.Now, options.MaxConnectionsPerAccount, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AQuietConnectionIsPingedWithTheLatestEventId()
    {
        options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
        await OpenAsync(new LiveRequest("", null, null, null));

        var id = feed.Publish(LiveEventTypes.BeepPosted, Json(Posted(Alice)));

        Assert.Equal(new LiveMessage(LiveMessages.Ping, "{}", id), await NextAsync());
    }

    [Fact]
    public async Task UnreadableEventsAreSkipped()
    {
        await OpenAsync(new LiveRequest("public", null, null, null));
        var beep = Posted(Alice);

        feed.Publish(LiveEventTypes.BeepPosted, "{not json");
        feed.Publish("from_a_newer_version", "{}");
        feed.Publish(LiveEventTypes.BeepPosted, Json(beep));

        Assert.Contains(beep.Id.ToString("D"), (await NextAsync()).Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStreamEndsWhenTheFeedDropsIt()
    {
        await OpenAsync(new LiveRequest("", null, null, null));

        feed.EndAll();

        Assert.False(await stream!.MoveNextAsync());
    }

    public async ValueTask DisposeAsync()
    {
        if (stream is not null)
        {
            await stream.DisposeAsync();
        }

        stop.Dispose();
    }

    private async Task<LiveMessage> OpenAsync(LiveRequest request)
    {
        var result = await CreateService().OpenAsync(TomViewer, request, stop.Token);
        Assert.True(result.IsSuccess, result.Error?.Code);
        stream = result.Value.GetAsyncEnumerator(stop.Token);
        return await NextAsync();
    }

    private async Task<LiveMessage> NextAsync()
    {
        Assert.True(await stream!.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct));
        return stream.Current;
    }

    private static BeepPostedEvent Posted(Guid authorAccount)
    {
        var id = Guid.NewGuid();
        return new BeepPostedEvent(id, authorAccount, authorAccount, null, null, id, []);
    }

    private static string Json(BeepPostedEvent beep) =>
        System.Text.Json.JsonSerializer.Serialize(beep, LiveJsonContext.Default.BeepPostedEvent);

    private LiveService CreateService()
    {
        var beeps = Substitute.For<IBeepStore>();
        var counters = Substitute.For<IBeepCounterStore>();
        var live = TestData.Live(store, counters);
        var views = new BeepViews(beeps, counters, TestData.Avatars, TestData.Media, NullLogger<BeepViews>.Instance);
        return new LiveService(
            feed,
            store,
            live,
            profiles,
            new NotificationService(notifications, beeps, views, live, time),
            Options.Create(options),
            time,
            new TestLocalizer(),
            NullLogger<LiveService>.Instance);
    }
}
