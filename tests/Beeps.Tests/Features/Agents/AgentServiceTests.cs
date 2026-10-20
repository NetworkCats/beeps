using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.RateLimiting;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Features.Agents;

public sealed class AgentServiceTests
{
    private readonly IRateLimitStore rateLimitStore = Substitute.For<IRateLimitStore>();

    public AgentServiceTests() =>
        rateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Allowed);

    private static readonly Guid OwnerId = Guid.CreateVersion7(TestData.Now);

    private readonly IAgentStore store = Substitute.For<IAgentStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly IOutbox outbox = Substitute.For<IOutbox>();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();
    private readonly INotificationStore notifications = Substitute.For<INotificationStore>();
    private readonly AgentOptions options = new() { MaxPerOwner = 3 };

    [Fact]
    public async Task CreatesAnUnnamedAgentWithAKeyStoredOnlyAsAHash()
    {
        AgentKey? stored = null;
        Actor? agent = null;
        store.When(s => s.AddKey(Arg.Any<AgentKey>())).Do(ci => stored = ci.Arg<AgentKey>());
        store.When(s => s.AddAgent(Arg.Any<Actor>())).Do(ci => agent = ci.Arg<Actor>());

        var result = await CreateService().CreateAsync(OwnerId, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.True(AgentKeys.IsWellFormed(result.Value.Key));
        Assert.Equal(AgentKeys.Hash(result.Value.Key), stored?.KeyHash);
        Assert.Equal(AgentKeys.DisplayPrefix(result.Value.Key), result.Value.Agent.KeyPrefix);
        Assert.Equal(ActorKind.Agent, agent?.Kind);
        Assert.Equal(OwnerId, agent?.OwnerId);
        Assert.Null(agent?.Handle);
        Assert.False(result.Value.Agent.IsConnected);
        Assert.Equal(new Uri($"https://beeps.test/avatars/{agent!.Id:D}.svg"), result.Value.Agent.AvatarUrl);
        Assert.Equal([AdvisoryLocks.OwnerAgents(OwnerId)], unitOfWork.Locks);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task TheLimitIsCheckedUnderTheOwnersLock()
    {
        store.CountAgentsAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(3);

        var result = await CreateService().CreateAsync(OwnerId, TestSupport.Ct);

        Assert.Equal(AgentErrorCodes.AgentLimitReached, result.Error?.Code);
        Assert.Equal("Error.AgentLimitReached(3)", result.Error?.Detail);
        store.DidNotReceiveWithAnyArgs().AddAgent(default!);
    }

    [Fact]
    public async Task RegeneratingRevokesTheOldKeyAndForgetsItsCache()
    {
        var agent = Agent();
        store.ListAgentsAsync(OwnerId, Arg.Any<CancellationToken>()).Returns([new AgentRow(agent.Id, "tom.claude", "Claude", "Claude", TestData.Now, null, "beeps_new", null)]);
        var result = await CreateService().RegenerateKeyAsync(OwnerId, agent.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Received.InOrder(() =>
        {
            store.RevokeKeysAsync(agent.Id, TestData.Now, Arg.Any<CancellationToken>());
            store.AddKey(Arg.Is<AgentKey>(k => k.AgentId == agent.Id && k.KeyHash == AgentKeys.Hash(result.Value.Key)));
            store.ForgetKeysAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == agent.Id));
        });
    }

    [Fact]
    public async Task ANewKeyComesWithTheAgentsOwnAvatar()
    {
        var agent = Agent();
        store.ListAgentsAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(
            [new AgentRow(agent.Id, "tom.claude", "Claude", "Claude", TestData.Now, null, "beeps_new", null, AvatarKey: "avatars/abc")]);

        var result = await CreateService().RegenerateKeyAsync(OwnerId, agent.Id, TestSupport.Ct);

        Assert.True(result.Value.Agent.HasCustomAvatar);
        Assert.Equal(TestData.Avatars.For(agent.Id, "avatars/abc"), result.Value.Agent.AvatarUrl);
    }

    [Fact]
    public async Task SomeoneElsesAgentIsNotFound()
    {
        var agent = Agent(owner: Guid.NewGuid());
        var service = CreateService();

        var regenerate = await service.RegenerateKeyAsync(OwnerId, agent.Id, TestSupport.Ct);
        var disable = await service.DisableAsync(OwnerId, agent.Id, TestSupport.Ct);
        var delete = await service.DeleteAsync(OwnerId, agent.Id, TestSupport.Ct);

        Assert.All([regenerate.Error, disable.Error, delete.Error], e => Assert.Equal(ErrorKind.NotFound, e?.Kind));
        Assert.Null(agent.DisabledAt);
        Assert.Null(agent.DeletedAt);
        await store.DidNotReceiveWithAnyArgs().RevokeKeysAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task DisableAndEnableTakeEffectImmediately()
    {
        var agent = Agent();
        var service = CreateService();

        await service.DisableAsync(OwnerId, agent.Id, TestSupport.Ct);
        var disabledAt = agent.DisabledAt;
        await service.EnableAsync(OwnerId, agent.Id, TestSupport.Ct);

        Assert.Equal(TestData.Now, disabledAt);
        Assert.Null(agent.DisabledAt);
        await store.Received(2).ForgetKeysAsync(Arg.Any<IReadOnlyCollection<Guid>>());
        Assert.Equal(2, unitOfWork.Saves);
    }

    [Fact]
    public async Task DeletingRevokesKeysAndKeepsTheRecord()
    {
        var agent = Agent();

        var result = await CreateService().DeleteAsync(OwnerId, agent.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.Now, agent.DeletedAt);
        await store.Received(1).RevokeKeysAsync(agent.Id, TestData.Now, Arg.Any<CancellationToken>());
        await store.Received(1).ForgetKeysAsync(Arg.Any<IReadOnlyCollection<Guid>>());
        outbox.Received(1).Enqueue(SearchAccountSyncRequest.MessageType, new SearchAccountSyncRequest(agent.Id, true), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<SearchAccountSyncRequest>>());
        Assert.Equal([$$"""{"actor_ids":["{{agent.Id:D}}"]}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.AuthorsRemoved));
        // The notifications it caused go with it: their recipients' unread counts are sent again.
        await notifications.Received(1).ListUnreadRecipientsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(b => b.Count == 0), Arg.Is<IReadOnlyCollection<Guid>>(a => a.Single() == agent.Id), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryChangeToTheAgentsRedrawsTheOwnersListsOfThem()
    {
        var agent = Agent();
        var row = new AgentWithOwnerRow(Guid.NewGuid(), null, null, null, TestData.Now, OwnerId, "tom", "Tom");
        store.GetAgentWithOwnerAsync(row.Id, Arg.Any<CancellationToken>()).Returns(row);
        store.TryConnectAsync(default, default!, default!, default!, TestSupport.Ct).ReturnsForAnyArgs(true);
        var service = CreateService();

        await service.CreateAsync(OwnerId, TestSupport.Ct);
        await service.RegenerateKeyAsync(OwnerId, agent.Id, TestSupport.Ct);
        await service.DisableAsync(OwnerId, agent.Id, TestSupport.Ct);
        await service.EnableAsync(OwnerId, agent.Id, TestSupport.Ct);
        await service.ConnectAsync(row.Id, "Claude", TestSupport.Ct);
        await service.DeleteAsync(OwnerId, agent.Id, TestSupport.Ct);
        // Someone else's agent changes nothing of the owner's.
        await service.DisableAsync(OwnerId, Agent(owner: Guid.NewGuid()).Id, TestSupport.Ct);

        Assert.Equal(Enumerable.Repeat($$"""{"owner_id":"{{OwnerId:D}}"}""", 6), TestData.LiveEvents(liveEvents, LiveEventTypes.AgentsChanged));
    }

    [Fact]
    public async Task MalformedKeysNeverReachTheStore()
    {
        var identity = await CreateService().AuthenticateAsync("beeps_nope", TestSupport.Ct);

        Assert.Null(identity);
        await store.DidNotReceiveWithAnyArgs().FindActiveKeyAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task UnknownKeysAreRejected()
    {
        Assert.Null(await CreateService().AuthenticateAsync(AgentKeys.Generate(), TestSupport.Ct));
    }

    [Fact]
    public async Task ALiveKeyIdentifiesItsAgentAndOwner()
    {
        var key = AgentKeys.Generate();
        var match = new AgentKeyMatch(Guid.NewGuid(), AgentKeys.Hash(key), Guid.NewGuid(), OwnerId, TestData.Now.AddMinutes(-1), Connected: true);
        store.FindActiveKeyAsync(match.KeyHash, Arg.Any<CancellationToken>()).Returns(match);

        var identity = await CreateService().AuthenticateAsync(key, TestSupport.Ct);

        Assert.Equal(new AgentIdentity(match.AgentId, OwnerId, Connected: true, match.KeyId), identity);
        await store.DidNotReceiveWithAnyArgs().TouchKeyAsync(default!, default, TestSupport.Ct);
    }

    [Fact]
    public async Task LastUsedIsRecordedAtMostEveryFewMinutes()
    {
        var key = AgentKeys.Generate();
        var match = new AgentKeyMatch(Guid.NewGuid(), AgentKeys.Hash(key), Guid.NewGuid(), OwnerId, TestData.Now - AgentService.LastUsedResolution, Connected: false);
        store.FindActiveKeyAsync(match.KeyHash, Arg.Any<CancellationToken>()).Returns(match);

        await CreateService().AuthenticateAsync(key, TestSupport.Ct);
        store.FindActiveKeyAsync(match.KeyHash, Arg.Any<CancellationToken>()).Returns(match with { LastUsedAt = TestData.Now });
        await CreateService().AuthenticateAsync(key, TestSupport.Ct);

        await store.Received(1).TouchKeyAsync(match, TestData.Now, Arg.Any<CancellationToken>());
        // The owner's agents page shows when; only the owner's.
        Assert.Equal([$$"""{"owner_id":"{{OwnerId:D}}","owner_only":true}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.AgentsChanged));
    }

    [Fact]
    public async Task ListAndGetOnlyShowTheOwnersAgents()
    {
        var row = new AgentRow(Guid.NewGuid(), "tom.claude", "tom's Claude", "Claude Opus 4.5", TestData.Now, null, "beeps_abcdef", null);
        store.ListAgentsAsync(OwnerId, Arg.Any<CancellationToken>()).Returns([row]);
        var service = CreateService();

        var list = await service.ListAsync(OwnerId, TestSupport.Ct);
        var found = await service.GetAsync(OwnerId, row.Id, TestSupport.Ct);
        var missing = await service.GetAsync(OwnerId, Guid.NewGuid(), TestSupport.Ct);

        Assert.True(Assert.Single(list).IsConnected);
        Assert.Equal(row.Id, found.Value.Id);
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
    }

    [Fact]
    public async Task SelfIncludesTheOwner()
    {
        var row = new AgentWithOwnerRow(Guid.NewGuid(), null, null, null, TestData.Now, OwnerId, "tom", "Tom");
        store.GetAgentWithOwnerAsync(row.Id, Arg.Any<CancellationToken>()).Returns(row);
        var service = CreateService();

        var self = await service.GetSelfAsync(row.Id, TestSupport.Ct);
        var missing = await service.GetSelfAsync(Guid.NewGuid(), TestSupport.Ct);

        Assert.Equal("tom", self.Value.OwnerHandle);
        Assert.Equal(new Uri($"https://beeps.test/avatars/{OwnerId:D}.svg"), self.Value.OwnerAvatarUrl);
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
    }

    [Fact]
    public async Task ConnectingNamesTheAgentAfterItsOwnerAndModel()
    {
        var row = new AgentWithOwnerRow(Guid.NewGuid(), null, null, null, TestData.Now, OwnerId, "tom", "Tom");
        store.GetAgentWithOwnerAsync(row.Id, Arg.Any<CancellationToken>()).Returns(row);
        store.ListHandlesStartingWithAsync("tom.claude", Arg.Any<CancellationToken>()).Returns(new HashSet<string> { "tom.claude", "tom.claude3" });
        store.TryConnectAsync(default, default!, default!, default!, TestSupport.Ct).ReturnsForAnyArgs(true);

        var result = await CreateService().ConnectAsync(row.Id, "  Claude Opus 4.5 ", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).TryConnectAsync(row.Id, "Claude Opus 4.5", "tom's Claude", "tom.claude2", Arg.Any<CancellationToken>());
        await store.Received(1).ForgetKeysAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == row.Id));
        Assert.Equal([AdvisoryLocks.OwnerAgents(OwnerId)], unitOfWork.Locks);
        // Findable from now on, and the owner's onboarding page hears of it.
        outbox.Received(1).Enqueue(SearchAccountSyncRequest.MessageType, new SearchAccountSyncRequest(row.Id, false), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<SearchAccountSyncRequest>>());
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal([$$"""{"agent_id":"{{row.Id:D}}","owner_id":"{{OwnerId:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.AgentConnected));
    }

    [Theory]
    [InlineData("GPT-5")]
    [InlineData(null)]
    [InlineData("bad\u0007name")]
    public async Task ConnectingAgainChangesNothing(string? modelName)
    {
        var row = new AgentWithOwnerRow(Guid.NewGuid(), "tom.claude", "tom's Claude", "Claude Opus 4.5", TestData.Now, OwnerId, "tom", "Tom");
        store.GetAgentWithOwnerAsync(row.Id, Arg.Any<CancellationToken>()).Returns(row);

        var result = await CreateService().ConnectAsync(row.Id, modelName, TestSupport.Ct);

        Assert.Equal("Claude Opus 4.5", result.Value.ModelName);
        await store.DidNotReceiveWithAnyArgs().TryConnectAsync(default, default!, default!, default!, TestSupport.Ct);
        Assert.Empty(unitOfWork.Locks);
        Assert.Empty(TestData.LiveEvents(liveEvents, LiveEventTypes.AgentConnected));
    }

    [Fact]
    public async Task LosingTheRaceToConnectKeepsTheWinnersNames()
    {
        var row = new AgentWithOwnerRow(Guid.NewGuid(), null, null, null, TestData.Now, OwnerId, "tom", "Tom");
        store.GetAgentWithOwnerAsync(row.Id, Arg.Any<CancellationToken>()).Returns(row);
        store.ListHandlesStartingWithAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new HashSet<string>());

        var result = await CreateService().ConnectAsync(row.Id, "Claude", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.DidNotReceiveWithAnyArgs().ForgetKeysAsync(default!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("bad\u0007name")]
    public async Task ConnectingNeedsAModelName(string? modelName)
    {
        var row = new AgentWithOwnerRow(Guid.NewGuid(), null, null, null, TestData.Now, OwnerId, "tom", "Tom");
        store.GetAgentWithOwnerAsync(row.Id, Arg.Any<CancellationToken>()).Returns(row);

        var result = await CreateService().ConnectAsync(row.Id, modelName, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.InvalidInput, result.Error?.Code);
        Assert.Equal(["Validation.ModelName(100)"], result.Error!.Fields![AgentFields.ModelName]);
        await store.DidNotReceiveWithAnyArgs().TryConnectAsync(default, default!, default!, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task ConnectingAMissingAgentIsNotFound()
    {
        var result = await CreateService().ConnectAsync(Guid.NewGuid(), "Claude", TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
    }

    // ---- Renaming ------------------------------------------------------------

    [Fact]
    public async Task OwnersRenameTheirConnectedAgents()
    {
        var agent = ConnectedAgent();
        store.ListAgentsAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(
            [new AgentRow(agent.Id, "tom.helper", "Helper", "Claude", TestData.Now, null, "beeps_abc", null, "bio")]);

        var result = await CreateService().UpdateProfileAsync(OwnerId, agent.Id, new AgentProfileRequest(" Helper ", "helper", " I help.\r\nMore. "), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(("Helper", "tom.helper", "tom.helper", "I help.\nMore."), (agent.DisplayName, agent.Handle, agent.NormalizedHandle, agent.Bio));
        Assert.Equal([AdvisoryLocks.OwnerAgents(OwnerId)], unitOfWork.Locks);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal("helper", result.Value.HandleName);
        outbox.Received(1).Enqueue(SearchAccountSyncRequest.MessageType, new SearchAccountSyncRequest(agent.Id, false), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<SearchAccountSyncRequest>>());
        Assert.Equal([$$"""{"actor_id":"{{agent.Id:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.ActorChanged));
    }

    [Fact]
    public async Task ARenameReadsTheAgentAgainUnderTheLock()
    {
        // Another rename committed while this one waited for the lock: the handle this one replaces is that one's.
        var agent = ConnectedAgent();
        store.ReloadAsync(agent, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.Equal([AdvisoryLocks.OwnerAgents(OwnerId)], unitOfWork.Locks);
            (agent.Handle, agent.NormalizedHandle) = ("tom.helper", "tom.helper");
            return Task.CompletedTask;
        });
        store.ListAgentsAsync(OwnerId, Arg.Any<CancellationToken>()).Returns([new AgentRow(agent.Id, "tom.claude", "x", "Claude", TestData.Now, null, null, null)]);

        var result = await CreateService().UpdateProfileAsync(OwnerId, agent.Id, new AgentProfileRequest("x", "claude", null), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        // Its handle is no longer the one asked for, so that one is checked.
        await store.Received(1).HandleTakenAsync("tom.claude", Arg.Any<CancellationToken>());
        Assert.Equal("tom.claude", agent.NormalizedHandle);
    }

    [Fact]
    public async Task KeepingTheHandleDoesNotCheckIt()
    {
        var agent = ConnectedAgent();
        store.ListAgentsAsync(OwnerId, Arg.Any<CancellationToken>()).Returns([new AgentRow(agent.Id, "tom.claude", "x", "Claude", TestData.Now, null, null, null)]);

        var result = await CreateService().UpdateProfileAsync(OwnerId, agent.Id, new AgentProfileRequest("New name", "claude", null), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.DidNotReceiveWithAnyArgs().HandleTakenAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task ATakenHandleIsRefusedCaseInsensitively()
    {
        var agent = ConnectedAgent();
        store.HandleTakenAsync("tom.gpt", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().UpdateProfileAsync(OwnerId, agent.Id, new AgentProfileRequest("x", "GPT", null), TestSupport.Ct);

        Assert.Equal(AgentErrorCodes.HandleTaken, result.Error?.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
        Assert.Equal(["Error.HandleTaken"], result.Error?.Fields?[AgentFields.HandleName]!);
        Assert.Equal("tom.claude", agent.Handle);
        Assert.Equal(1, unitOfWork.Rollbacks);
    }

    [Fact]
    public async Task LosingTheRaceForAHandleIsAlsoTaken()
    {
        var agent = ConnectedAgent();
        unitOfWork.FailNextSave = UniqueViolationException.ForIndex(AccountIndexes.UniqueHandle, new InvalidOperationException());

        var result = await CreateService().UpdateProfileAsync(OwnerId, agent.Id, new AgentProfileRequest("x", "helper", null), TestSupport.Ct);

        Assert.Equal(AgentErrorCodes.HandleTaken, result.Error?.Code);
    }

    [Fact]
    public async Task AnAgentThatNeverConnectedHasNoNameToChange()
    {
        var agent = Agent();

        var result = await CreateService().UpdateProfileAsync(OwnerId, agent.Id, new AgentProfileRequest("x", "helper", null), TestSupport.Ct);

        Assert.Equal(AgentErrorCodes.AgentNotConnected, result.Error?.Code);
        Assert.Null(agent.Handle);
    }

    [Fact]
    public async Task SomeoneElsesAgentCannotBeRenamed()
    {
        var agent = ConnectedAgent(owner: Guid.NewGuid());

        var result = await CreateService().UpdateProfileAsync(OwnerId, agent.Id, new AgentProfileRequest("x", "helper", null), TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
        Assert.Equal("tom.claude", agent.Handle);
    }

    [Fact]
    public async Task ABadRenameNamesEveryBadField()
    {
        var result = await CreateService().UpdateProfileAsync(OwnerId, Guid.NewGuid(), new AgentProfileRequest("", "no dots.here", new string('x', 301)), TestSupport.Ct);

        Assert.Equal([ProfileFields.Bio, ProfileFields.DisplayName, AgentFields.HandleName], result.Error!.Fields!.Keys.Order(StringComparer.Ordinal));
        await store.DidNotReceiveWithAnyArgs().FindAgentAsync(default, TestSupport.Ct);
    }

    private Actor ConnectedAgent(Guid? owner = null)
    {
        var agent = Agent(owner);
        agent.Handle = "tom.claude";
        agent.NormalizedHandle = "tom.claude";
        agent.DisplayName = "tom's Claude";
        agent.ModelName = "Claude";
        return agent;
    }

    private Actor Agent(Guid? owner = null)
    {
        var agent = new Actor { Id = Guid.NewGuid(), Kind = ActorKind.Agent, OwnerId = owner ?? OwnerId, CreatedAt = TestData.Now };
        store.FindAgentAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        return agent;
    }

    private AgentService CreateService() => new(
        store,
        unitOfWork,
        outbox,
        TestData.Live(liveEvents),
        TestData.Inboxes(liveEvents, notifications),
        TestData.RateLimits(rateLimitStore),
        TestData.Avatars,
        Options.Create(options),
        TestData.Time(),
        new TestLocalizer(),
        NullLogger<AgentService>.Instance);

    [Fact]
    public async Task ChangesToAgentsCountAgainstTheOwnersQuota()
    {
        var agent = Agent();
        rateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromMinutes(5)));
        var service = CreateService();

        Error?[] errors =
        [
            (await service.CreateAsync(OwnerId, TestSupport.Ct)).Error,
            (await service.RegenerateKeyAsync(OwnerId, agent.Id, TestSupport.Ct)).Error,
            (await service.DisableAsync(OwnerId, agent.Id, TestSupport.Ct)).Error,
            (await service.DeleteAsync(OwnerId, agent.Id, TestSupport.Ct)).Error,
        ];

        Assert.All(errors, e => Assert.Equal(ErrorKind.RateLimited, e?.Kind));
        Assert.Equal(0, unitOfWork.Saves);
        await rateLimitStore.Received(4).TryAcquireAsync(
            Arg.Is<IReadOnlyList<RateLimitCounter>>(c => TestData.Policies(c).Single() == RateLimitPolicies.AccountChange),
            Arg.Any<CancellationToken>());
    }
}
