using Beeps.Features.Accounts;
using Beeps.Features.Agents;
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
    private static readonly Guid TomSession = Guid.NewGuid();
    private static readonly string LastSeen = new LiveEventId(TestData.Now.ToUnixTimeMilliseconds(), 7).ToString();

    private readonly TestLiveFeed feed = new();
    private readonly ILiveEventStore store = Substitute.For<ILiveEventStore>();
    private readonly IProfileStore profiles = Substitute.For<IProfileStore>();
    private readonly ISessionStore sessions = Substitute.For<ISessionStore>();
    private readonly IAgentStore agents = Substitute.For<IAgentStore>();
    private readonly AdminWatchers adminWatchers = new();
    private readonly INotificationStore notifications = Substitute.For<INotificationStore>();
    private readonly LiveOptions options = new() { HeartbeatInterval = TimeSpan.FromHours(1) };
    private readonly FixedTimeProvider time = new(TestData.Now);
    private readonly CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
    private IAsyncEnumerator<LiveMessage>? stream;
    private int unread;

    public LiveServiceTests()
    {
        store.AddConnectionAsync(default, default!, default, default, TestSupport.Ct).ReturnsForAnyArgs([]);
        profiles.ListFollowedCachedAsync(Tom, Arg.Any<CancellationToken>()).Returns([Alice]);
        notifications.CountUnreadAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs(_ => unread);
    }

    [Theory]
    [InlineData("nope", null, null, LiveErrorCodes.InvalidWatch)]
    [InlineData("home", "not-a-guid", null, LiveErrorCodes.InvalidBeeps)]
    [InlineData("home", null, "yesterday", LiveErrorCodes.InvalidEventId)]
    public async Task BadSubscriptionsAreRejected(string watch, string? beeps, string? lastEventId, string code)
    {
        var result = await CreateService().OpenAsync(TomViewer, new LiveSession(TomSession, Verified: true), new LiveRequest(watch, beeps, lastEventId, null), TestSupport.Ct);

        Assert.Equal(code, result.Error?.Code);
        Assert.Equal(ErrorKind.Validation, result.Error?.Kind);
    }

    [Fact]
    public async Task APageMayListOnlySoManyBeeps()
    {
        var beeps = string.Join(',', Enumerable.Range(0, LiveService.MaxWatchedBeeps + 1).Select(_ => Guid.NewGuid()));

        var result = await CreateService().OpenAsync(TomViewer, new LiveSession(TomSession, Verified: true), new LiveRequest("", beeps, null, null), TestSupport.Ct);

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
        await store.Received(1).AddConnectionAsync(Tom, connectionId, TestData.Now, options.MaxConnectionsPerActor, Arg.Any<CancellationToken>());
        await store.Received(1).RemoveConnectionAsync(Tom, connectionId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APageComingBackFromItsLastEventIsNotCountedAgain()
    {
        var first = await OpenAsync(new LiveRequest("home", null, LastSeen, null));

        // It has the count; what changed it since is replayed.
        Assert.Equal(new LiveMessage(LiveMessages.Ping, "{}", LastSeen, options.ReconnectDelay), first);
        await notifications.DidNotReceiveWithAnyArgs().CountUnreadAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task AReconnectionReplaysFromItsLastEventAndAFreshPageFromWhenItWasRendered()
    {
        await OpenAsync(new LiveRequest("", null, LastSeen, TestData.Now.ToUnixTimeMilliseconds()));
        await stream!.DisposeAsync();
        await OpenAsync(new LiveRequest("", null, null, TestData.Now.ToUnixTimeMilliseconds()));

        Assert.Equal(LiveEventId.Parse(LastSeen), feed.Starts[0]);
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

        Assert.Equal(new LiveMessage(LiveMessages.Beep, $$"""{"id":"{{followed.Id:D}}"}""", id), beep);
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
    public async Task RemovedAuthorsGoFromEveryPage()
    {
        await OpenAsync(new LiveRequest("", "", null, null));
        var (banned, agent) = (Guid.NewGuid(), Guid.NewGuid());

        var id = feed.Publish(LiveEventTypes.AuthorsRemoved, $$"""{"actor_ids":["{{banned:D}}","{{agent:D}}"]}""");

        Assert.Equal(new LiveMessage(LiveMessages.AuthorsRemoved, $$"""{"ids":["{{banned:D}}","{{agent:D}}"]}""", id), await NextAsync());
    }

    [Fact]
    public async Task AHiddenBeepIsRemovedForEveryoneButItsAuthorAndTheirOwnerWhoSeeItMarked()
    {
        var (own, agents, others) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await OpenAsync(new LiveRequest("", $"{own},{agents},{others}", null, null));

        feed.Publish(LiveEventTypes.BeepHidden, $$"""{"id":"{{Guid.NewGuid():D}}","author_id":"{{Alice:D}}","author_owner_id":null}""");
        feed.Publish(LiveEventTypes.BeepHidden, $$"""{"id":"{{own:D}}","author_id":"{{Tom:D}}","author_owner_id":null}""");
        var first = await NextAsync();
        feed.Publish(LiveEventTypes.BeepHidden, $$"""{"id":"{{agents:D}}","author_id":"{{Guid.NewGuid():D}}","author_owner_id":"{{Tom:D}}"}""");
        var second = await NextAsync();
        feed.Publish(LiveEventTypes.BeepHidden, $$"""{"id":"{{others:D}}","author_id":"{{Alice:D}}","author_owner_id":null}""");
        var third = await NextAsync();

        Assert.Equal((LiveMessages.Changed, $$"""{"ids":["{{own:D}}"]}"""), (first.Type, first.Data));
        Assert.Equal((LiveMessages.Changed, $$"""{"ids":["{{agents:D}}"]}"""), (second.Type, second.Data));
        Assert.Equal((LiveMessages.Removed, $$"""{"id":"{{others:D}}"}"""), (third.Type, third.Data));
    }

    [Fact]
    public async Task TheInboxPageFollowsTheBeepsOfNewNotifications()
    {
        var (mentioned, elsewhere) = (Guid.NewGuid(), Guid.NewGuid());
        await OpenAsync(new LiveRequest("notifications", null, null, null));

        feed.Publish(LiveEventTypes.Inbox, $$"""{"recipient_ids":["{{Alice:D}}"],"beep_ids":["{{elsewhere:D}}"]}""");
        feed.Publish(LiveEventTypes.Inbox, $$"""{"recipient_ids":["{{Tom:D}}"],"beep_ids":["{{mentioned:D}}"]}""");
        Assert.Equal(LiveMessages.Inbox, (await NextAsync()).Type);
        feed.Publish(LiveEventTypes.Counts, $$"""{"beeps":[{"id":"{{elsewhere:D}}","like_count":1,"reply_count":0},{"id":"{{mentioned:D}}","like_count":2,"reply_count":0}]}""");

        Assert.Equal($$"""[{"id":"{{mentioned:D}}","like_count":2,"reply_count":0}]""", (await NextAsync()).Data);
    }

    [Fact]
    public async Task ATabWhoseSessionEndedSignsOutAndItsStreamEnds()
    {
        await OpenAsync(new LiveRequest("home", null, null, null));
        sessions.IsActiveAsync(TomSession, TestData.Now, Arg.Any<CancellationToken>()).Returns(true, false);

        // Another account's sessions, and one of Tom's other sessions: this tab is still signed in, its devices page drops it.
        feed.Publish(LiveEventTypes.SessionsEnded, $$"""{"account_id":"{{Alice:D}}"}""");
        var other = feed.Publish(LiveEventTypes.SessionsEnded, $$"""{"account_id":"{{Tom:D}}"}""");
        var id = feed.Publish(LiveEventTypes.SessionsEnded, $$"""{"account_id":"{{Tom:D}}"}""");

        Assert.Equal(new LiveMessage(LiveMessages.Sessions, "{}", other), await NextAsync());
        Assert.Equal(new LiveMessage(LiveMessages.SignedOut, "{}", id), await NextAsync());
        Assert.False(await stream!.MoveNextAsync());
        await sessions.Received(2).IsActiveAsync(TomSession, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATabWhoseSessionRanOutWithNoEventSignsOutOnTheNextHeartbeat()
    {
        options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
        await OpenAsync(new LiveRequest("home", null, null, null));
        sessions.IsActiveAsync(TomSession, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

        Assert.Equal(LiveMessages.Ping, (await NextAsync()).Type);
        time.Now += options.SessionCheckInterval;

        Assert.Equal(new LiveMessage(LiveMessages.SignedOut, "{}"), await NextAsync());
        Assert.False(await stream!.MoveNextAsync());
    }

    [Fact]
    public async Task ALogInElsewhereIsListedOnTheDevicesPage()
    {
        await OpenAsync(new LiveRequest("", null, null, null));

        feed.Publish(LiveEventTypes.SessionStarted, $$"""{"account_id":"{{Alice:D}}"}""");
        var id = feed.Publish(LiveEventTypes.SessionStarted, $$"""{"account_id":"{{Tom:D}}"}""");

        Assert.Equal(new LiveMessage(LiveMessages.Sessions, "{}", id), await NextAsync());
    }

    [Fact]
    public async Task ALanguageOrRightsChangeShowsTheAccountsTabsAnew()
    {
        await OpenAsync(new LiveRequest("", null, null, null));

        feed.Publish(LiveEventTypes.AccountChanged, $$"""{"account_id":"{{Alice:D}}"}""");
        var id = feed.Publish(LiveEventTypes.AccountChanged, $$"""{"account_id":"{{Tom:D}}"}""");

        Assert.Equal(new LiveMessage(LiveMessages.Reload, "{}", id), await NextAsync());
    }

    [Fact]
    public async Task AColourSchemeChangeReachesTheAccountsTabs()
    {
        await OpenAsync(new LiveRequest("", null, null, null));

        feed.Publish(LiveEventTypes.ThemeChanged, $$"""{"account_id":"{{Alice:D}}","theme":"light"}""");
        var id = feed.Publish(LiveEventTypes.ThemeChanged, $$"""{"account_id":"{{Tom:D}}","theme":"dark"}""");

        Assert.Equal(new LiveMessage(LiveMessages.Theme, """{"theme":"dark"}""", id), await NextAsync());
    }

    [Fact]
    public async Task APageRenderedAfterTheChangeIsNotShownAnewForIt()
    {
        // The test feed stamps its events at TestData.Now; a fresh page is replayed from a little before it was rendered.
        await OpenAsync(new LiveRequest("", null, null, TestData.Now.ToUnixTimeMilliseconds() + 1));

        feed.Publish(LiveEventTypes.AccountChanged, $$"""{"account_id":"{{Tom:D}}"}""");
        var id = feed.Publish(LiveEventTypes.Inbox, $$"""{"recipient_ids":["{{Tom:D}}"]}""");

        Assert.Equal((LiveMessages.Inbox, id), ((await NextAsync()).Type, stream!.Current.Id));
    }

    [Fact]
    public async Task APageRenderedPastTheLastTimeThereIsIsFollowedAllTheSame()
    {
        var message = await OpenAsync(new LiveRequest("", null, null, long.MaxValue));

        Assert.NotNull(message);
    }

    [Fact]
    public async Task APageRenderedAsTheChangeHappenedIsShownAnewForIt()
    {
        await OpenAsync(new LiveRequest("", null, null, TestData.Now.ToUnixTimeMilliseconds()));

        var id = feed.Publish(LiveEventTypes.AccountChanged, $$"""{"account_id":"{{Tom:D}}"}""");

        Assert.Equal(new LiveMessage(LiveMessages.Reload, "{}", id), await NextAsync());
    }

    [Fact]
    public async Task SearchPagesAskAgainWhenTheIndexChanges()
    {
        await OpenAsync(new LiveRequest("", null, null, null));
        await using var search = await OpenOtherAsync(new LiveRequest("search", null, null, null));

        var id = feed.Publish(LiveEventTypes.SearchChanged, "{}");
        feed.Publish(LiveEventTypes.AccountChanged, $$"""{"account_id":"{{Tom:D}}"}""");

        Assert.Equal(new LiveMessage(LiveMessages.Search, "{}", id), await NextAsync(search));
        // The page without search results skipped it.
        Assert.Equal(LiveMessages.Reload, (await NextAsync()).Type);
    }

    [Fact]
    public async Task AnOpenAdminPageIsCountedAsWatchingUntilItCloses()
    {
        await OpenAsync(new LiveRequest("home", null, null, null));
        Assert.False(adminWatchers.Any);
        await using var admin = await OpenOtherAsync(new LiveRequest("admin", null, null, null));

        Assert.True(adminWatchers.Any);
        await admin.DisposeAsync();
        Assert.False(adminWatchers.Any);
    }

    [Fact]
    public async Task OnlyAnAdminFollowsTheAdminPages()
    {
        var result = await CreateService().OpenAsync(TomViewer, new LiveSession(TomSession, Verified: true), new LiveRequest("admin", null, null, null), stop.Token);
        stream = result.Value.GetAsyncEnumerator(stop.Token);
        await NextAsync();

        Assert.False(adminWatchers.Any);
    }

    [Fact]
    public async Task AdminPagesHearOfAnyChangeToTheSite()
    {
        await OpenAsync(new LiveRequest("admin", null, null, null));

        feed.Publish(LiveEventTypes.Counts, $$"""{"beeps":[{"id":"{{Guid.NewGuid():D}}","like_count":1,"reply_count":0}]}""");
        feed.Publish(LiveEventTypes.LikeChanged, $$"""{"beep_id":"{{Guid.NewGuid():D}}","actor_id":"{{Alice:D}}","liked":true}""");
        var posted = feed.Publish(LiveEventTypes.BeepPosted, Json(Posted(Alice)));
        var reported = feed.Publish(LiveEventTypes.AdminChanged, "{}");

        Assert.Equal(new LiveMessage(LiveMessages.Admin, "{}", posted), await NextAsync());
        Assert.Equal(new LiveMessage(LiveMessages.Admin, "{}", reported), await NextAsync());
    }

    [Fact]
    public async Task ChangedBeepsAreRedrawnWhereTheyAreShown()
    {
        var (shown, other) = (Guid.NewGuid(), Guid.NewGuid());
        await OpenAsync(new LiveRequest("", $"{shown}", null, null));

        feed.Publish(LiveEventTypes.BeepsChanged, $$"""{"ids":["{{other:D}}"]}""");
        var id = feed.Publish(LiveEventTypes.BeepsChanged, $$"""{"ids":["{{other:D}}","{{shown:D}}"]}""");

        Assert.Equal(new LiveMessage(LiveMessages.Changed, $$"""{"ids":["{{shown:D}}"]}""", id), await NextAsync());
    }

    [Fact]
    public async Task OnlyTheViewersOwnLikesPressTheirButtons()
    {
        var shown = Guid.NewGuid();
        await OpenAsync(new LiveRequest("", $"{shown}", null, null));

        feed.Publish(LiveEventTypes.LikeChanged, $$"""{"beep_id":"{{shown:D}}","actor_id":"{{Alice:D}}","liked":true}""");
        feed.Publish(LiveEventTypes.LikeChanged, $$"""{"beep_id":"{{Guid.NewGuid():D}}","actor_id":"{{Tom:D}}","liked":true}""");
        var id = feed.Publish(LiveEventTypes.LikeChanged, $$"""{"beep_id":"{{shown:D}}","actor_id":"{{Tom:D}}","liked":false}""");

        Assert.Equal(new LiveMessage(LiveMessages.Liked, $$"""{"id":"{{shown:D}}","liked":false}""", id), await NextAsync());
    }

    [Fact]
    public async Task FollowingSomeoneBringsTheirBeepsToAnOpenHomeTimeline()
    {
        var bob = Guid.NewGuid();
        await OpenAsync(new LiveRequest("home", null, null, null));
        profiles.ListFollowedAsync(Tom, Arg.Any<CancellationToken>()).Returns([Alice, bob]);
        unread = 4;

        feed.Publish(LiveEventTypes.RelationChanged, $$"""{"actor_ids":["{{Alice:D}}","{{Guid.NewGuid():D}}"]}""");
        var id = feed.Publish(LiveEventTypes.RelationChanged, $$"""{"actor_ids":["{{Tom:D}}","{{bob:D}}"]}""");
        var resync = await NextAsync();
        var inbox = await NextAsync();
        var beep = Posted(bob);
        feed.Publish(LiveEventTypes.BeepPosted, Json(beep));

        Assert.Equal(new LiveMessage(LiveMessages.Resync, "{}", id), resync);
        Assert.Equal(new LiveMessage(LiveMessages.Inbox, """{"unread_count":4}""", id), inbox);
        Assert.Contains(beep.Id.ToString("D"), (await NextAsync()).Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryPageCatchesUpWhenBeepsComeBackAndOnlyOwnersHearOfTheirKeys()
    {
        // An older page of a list (no topic) catches up too: it draws its list again.
        await OpenAsync(new LiveRequest("", null, null, null));
        await using (var listing = await OpenOtherAsync(new LiveRequest($"thread:{Guid.NewGuid():D}", null, null, null)))
        {
            var id = feed.Publish(LiveEventTypes.Resync, "{}");
            // Alice's key use is for Alice's eyes only.
            feed.Publish(LiveEventTypes.AgentsChanged, $$"""{"owner_id":"{{Alice:D}}","owner_only":true}""");
            feed.Publish(LiveEventTypes.AgentsChanged, $$"""{"owner_id":"{{Alice:D}}"}""");

            Assert.Equal(new LiveMessage(LiveMessages.Resync, "{}", id), await NextAsync(listing));
            Assert.Equal(new LiveMessage(LiveMessages.Resync, "{}", id), await NextAsync());
        }

        var agents = await NextAsync();
        Assert.Equal((LiveMessages.Agents, $$"""{"id":"{{Alice:D}}"}"""), (agents.Type, agents.Data));
    }

    [Fact]
    public async Task AnUnverifiedTabFollowsOnlyItsAccount()
    {
        stream = (await CreateService().OpenAsync(TomViewer, new LiveSession(TomSession, Verified: false), new LiveRequest("home", null, null, null), stop.Token)).Value.GetAsyncEnumerator(stop.Token);
        await NextAsync();

        feed.Publish(LiveEventTypes.BeepPosted, Json(Posted(Alice)));
        feed.Publish(LiveEventTypes.Resync, "{}");
        feed.Publish(LiveEventTypes.ActorChanged, $$"""{"actor_id":"{{Alice:D}}"}""");
        var id = feed.Publish(LiveEventTypes.AccountChanged, $$"""{"account_id":"{{Tom:D}}"}""");

        Assert.Equal(new LiveMessage(LiveMessages.Reload, "{}", id), await NextAsync());
    }

    [Fact]
    public async Task AnUnverifiedTabHearsOfMissedEventsAndOfASignOutAmongThem()
    {
        stream = (await CreateService().OpenAsync(TomViewer, new LiveSession(TomSession, Verified: false), new LiveRequest("", null, null, null), stop.Token)).Value.GetAsyncEnumerator(stop.Token);
        await NextAsync();
        sessions.IsActiveAsync(TomSession, TestData.Now, Arg.Any<CancellationToken>()).Returns(true, false);

        // Still signed in: a reset only (no unread count to send), after which the page asks how it is signed in.
        var caughtUp = feed.Publish(LiveEventTypes.Gap, "");
        Assert.Equal(new LiveMessage(LiveMessages.Reset, "{}", caughtUp), await NextAsync());
        feed.Publish(LiveEventTypes.Gap, "");
        Assert.Equal(new LiveMessage(LiveMessages.SignedOut, "{}"), await NextAsync());
        Assert.False(await stream!.MoveNextAsync());
    }

    [Fact]
    public async Task APageWithMoreBeepsThanItListsHearsOfEveryBeep()
    {
        await OpenAsync(new LiveRequest("", null, null, null, All: true));

        var id = feed.Publish(LiveEventTypes.BeepRemoved, $$"""{"id":"{{Guid.NewGuid():D}}"}""");

        Assert.Equal((LiveMessages.Removed, id), ((await NextAsync()).Type, stream!.Current.Id));
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
    public async Task OwnersHearTheirAgentsConnectAndEveryPageHearsOfProfileChanges()
    {
        var agent = Guid.NewGuid();
        await OpenAsync(new LiveRequest("home", null, null, null));

        feed.Publish(LiveEventTypes.AgentConnected, $$"""{"agent_id":"{{Guid.NewGuid():D}}","owner_id":"{{Alice:D}}"}""");
        feed.Publish(LiveEventTypes.AgentConnected, $$"""{"agent_id":"{{agent:D}}","owner_id":"{{Tom:D}}"}""");
        var connected = await NextAsync();
        // Names and avatars show anywhere, so the page itself decides whether it shows Alice.
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
    public async Task MissedEventsMakeThePageCatchUpTheUnreadCountWithIt()
    {
        await OpenAsync(new LiveRequest("home", null, LastSeen, null));
        sessions.IsActiveAsync(TomSession, TestData.Now, Arg.Any<CancellationToken>()).Returns(true);
        unread = 7;

        var caughtUp = feed.Publish(LiveEventTypes.Gap, "");

        // The reset says where the stream stands now: a reconnect after it does not start from before the gap again.
        Assert.Equal(new LiveMessage(LiveMessages.Reset, "{}", caughtUp), await NextAsync());
        Assert.Equal(new LiveMessage(LiveMessages.Inbox, """{"unread_count":7}"""), await NextAsync());
    }

    [Fact]
    public async Task ASignOutAmongTheMissedEventsStillSignsTheTabOut()
    {
        await OpenAsync(new LiveRequest("home", null, LastSeen, null));
        sessions.IsActiveAsync(TomSession, TestData.Now, Arg.Any<CancellationToken>()).Returns(false);

        feed.Publish(LiveEventTypes.Gap, "");

        Assert.Equal(new LiveMessage(LiveMessages.SignedOut, "{}"), await NextAsync());
        Assert.False(await stream!.MoveNextAsync());
    }

    [Fact]
    public async Task AnAgentsUnreadCountFollowsItsOwnersBlocks()
    {
        var agent = Viewer.Agent(Guid.NewGuid(), Tom);
        unread = 3;
        stream = CreateService().OpenInbox(agent, null, null, stop.Token).Value.GetAsyncEnumerator(stop.Token);
        await NextAsync();

        feed.Publish(LiveEventTypes.RelationChanged, $$"""{"actor_ids":["{{Guid.NewGuid():D}}","{{Guid.NewGuid():D}}"]}""");
        unread = 2;
        var blocked = feed.Publish(LiveEventTypes.RelationChanged, $$"""{"actor_ids":["{{Tom:D}}","{{Guid.NewGuid():D}}"]}""");

        Assert.Equal(new LiveMessage(LiveMessages.Inbox, """{"unread_count":2}""", blocked), await NextAsync());
    }

    [Fact]
    public async Task AnAgentsStreamCarriesOnlyItsInbox()
    {
        var agent = Viewer.Agent(Guid.NewGuid(), Tom);
        unread = 1;
        stream = CreateService().OpenInbox(agent, null, null, stop.Token).Value.GetAsyncEnumerator(stop.Token);
        var first = await NextAsync();

        feed.Publish(LiveEventTypes.BeepPosted, Json(Posted(Tom)));
        feed.Publish(LiveEventTypes.AgentConnected, $$"""{"agent_id":"{{agent.ActorId:D}}","owner_id":"{{Tom:D}}"}""");
        feed.Publish(LiveEventTypes.ActorChanged, $$"""{"actor_id":"{{agent.ActorId:D}}"}""");
        feed.Publish(LiveEventTypes.Resync, "{}");
        unread = 2;
        feed.Publish(LiveEventTypes.Inbox, $$"""{"recipient_ids":["{{agent.ActorId:D}}"]}""");
        var second = await NextAsync();
        feed.Publish(LiveEventTypes.Gap, "");
        var afterGap = await NextAsync();

        Assert.Equal("""{"unread_count":1}""", first.Data);
        Assert.Equal((LiveMessages.Inbox, """{"unread_count":2}"""), (second.Type, second.Data));
        Assert.Equal(LiveMessages.Inbox, afterGap.Type);
        await store.Received(1).AddConnectionAsync(agent.ActorId, Arg.Any<string>(), TestData.Now, options.MaxConnectionsPerActor, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAgentsStreamEndsWhenItsKeyStopsWorking()
    {
        var (agent, key) = (Viewer.Agent(Guid.NewGuid(), Tom), Guid.NewGuid());
        agents.IsKeyActiveAsync(key, Arg.Any<CancellationToken>()).Returns(true, false);
        stream = CreateService().OpenInbox(agent, key, null, stop.Token).Value.GetAsyncEnumerator(stop.Token);
        await NextAsync();

        // Someone else's agents, and a change that leaves this key working (another agent disabled, say).
        feed.Publish(LiveEventTypes.AgentsChanged, $$"""{"owner_id":"{{Alice:D}}"}""");
        feed.Publish(LiveEventTypes.AgentsChanged, $$"""{"owner_id":"{{Tom:D}}","owner_only":true}""");
        feed.Publish(LiveEventTypes.AgentsChanged, $$"""{"owner_id":"{{Tom:D}}"}""");
        // The key was regenerated.
        var id = feed.Publish(LiveEventTypes.AgentsChanged, $$"""{"owner_id":"{{Tom:D}}"}""");

        Assert.Equal(new LiveMessage(LiveMessages.Close, "{}", id), await NextAsync());
        Assert.False(await stream!.MoveNextAsync());
        await agents.Received(2).IsKeyActiveAsync(key, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(LiveEventTypes.AuthorsRemoved, true)]
    [InlineData(LiveEventTypes.SessionsEnded, false)]
    public async Task AnAgentsStreamEndsWhenItOrItsOwnerIsRemoved(string type, bool agentItself)
    {
        var (agent, key) = (Viewer.Agent(Guid.NewGuid(), Tom), Guid.NewGuid());
        agents.IsKeyActiveAsync(key, Arg.Any<CancellationToken>()).Returns(false);
        stream = CreateService().OpenInbox(agent, key, null, stop.Token).Value.GetAsyncEnumerator(stop.Token);
        await NextAsync();

        var id = feed.Publish(type, type == LiveEventTypes.AuthorsRemoved
            ? $$"""{"actor_ids":["{{(agentItself ? agent.ActorId : Tom):D}}"]}"""
            : $$"""{"account_id":"{{Tom:D}}"}""");

        Assert.Equal(new LiveMessage(LiveMessages.Close, "{}", id), await NextAsync());
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
    public async Task AConnectionOthersEventsKeepBusyIsStillPinged()
    {
        options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
        await OpenAsync(new LiveRequest("", null, null, null));
        using var busy = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
        var publishing = Task.Run(
            async () =>
            {
                while (!busy.IsCancellationRequested)
                {
                    feed.Publish(LiveEventTypes.BeepPosted, Json(Posted(Alice)));
                    await Task.Delay(10, CancellationToken.None);
                }
            },
            CancellationToken.None);

        var message = await NextAsync();
        await busy.CancelAsync();
        await publishing;

        Assert.Equal(LiveMessages.Ping, message.Type);
        Assert.NotNull(message.Id);
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
        var result = await CreateService().OpenAsync(TomViewer, new LiveSession(TomSession, Verified: true, Admin: true), request, stop.Token);
        Assert.True(result.IsSuccess, result.Error?.Code);
        stream = result.Value.GetAsyncEnumerator(stop.Token);
        return await NextAsync();
    }

    private async Task<LiveMessage> NextAsync() => await NextAsync(stream!);

    private static async Task<LiveMessage> NextAsync(IAsyncEnumerator<LiveMessage> messages)
    {
        Assert.True(await messages.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct));
        return messages.Current;
    }

    /// <summary>A second tab of Tom's, already past its first message.</summary>
    private async Task<IAsyncEnumerator<LiveMessage>> OpenOtherAsync(LiveRequest request)
    {
        var result = await CreateService().OpenAsync(TomViewer, new LiveSession(TomSession, Verified: true, Admin: true), request, stop.Token);
        Assert.True(result.IsSuccess, result.Error?.Code);
        var other = result.Value.GetAsyncEnumerator(stop.Token);
        await NextAsync(other);
        return other;
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
        var views = TestServices.Views(beeps, counters);
        return new LiveService(
            feed,
            store,
            live,
            profiles,
            sessions,
            agents,
            new NotificationService(notifications, beeps, views, live, time),
            adminWatchers,
            Options.Create(options),
            time,
            new TestLocalizer(),
            NullLogger<LiveService>.Instance);
    }
}
