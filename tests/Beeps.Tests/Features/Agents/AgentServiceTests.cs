using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Profiles;
using Beeps.Features.Avatars;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Features.Agents;

public sealed class AgentServiceTests
{
    private static readonly Guid OwnerId = Guid.CreateVersion7(TestData.Now);

    private readonly IAgentStore store = Substitute.For<IAgentStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
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
        var result = await CreateService().RegenerateKeyAsync(OwnerId, agent.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Received.InOrder(() =>
        {
            store.RevokeKeysAsync(agent.Id, TestData.Now, Arg.Any<CancellationToken>());
            store.AddKey(Arg.Is<AgentKey>(k => k.AgentId == agent.Id && k.KeyHash == AgentKeys.Hash(result.Value.Key)));
            store.ForgetKeysAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == agent.Id), Arg.Any<CancellationToken>());
        });
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
        await store.Received(2).ForgetKeysAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
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
        await store.Received(1).ForgetKeysAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
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

        Assert.Equal(new AgentIdentity(match.AgentId, OwnerId, Connected: true), identity);
        await store.DidNotReceiveWithAnyArgs().TouchKeyAsync(default!, default, TestSupport.Ct);
    }

    [Fact]
    public async Task LastUsedIsRecordedAtMostEveryFewMinutes()
    {
        var key = AgentKeys.Generate();
        var match = new AgentKeyMatch(Guid.NewGuid(), AgentKeys.Hash(key), Guid.NewGuid(), OwnerId, TestData.Now - AgentService.LastUsedResolution, Connected: false);
        store.FindActiveKeyAsync(match.KeyHash, Arg.Any<CancellationToken>()).Returns(match);

        await CreateService().AuthenticateAsync(key, TestSupport.Ct);

        await store.Received(1).TouchKeyAsync(match, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAndGetOnlyShowTheOwnersAgents()
    {
        var row = new AgentRow(Guid.NewGuid(), "tom.claude", "tom's Claude", "Claude Opus 4.5", TestData.Now, null, "beeps_abcdef", TestData.Now, null);
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
        await store.Received(1).ForgetKeysAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == row.Id), Arg.Any<CancellationToken>());
        Assert.Equal([AdvisoryLocks.OwnerAgents(OwnerId)], unitOfWork.Locks);
    }

    [Fact]
    public async Task ConnectingAgainChangesNothing()
    {
        var row = new AgentWithOwnerRow(Guid.NewGuid(), "tom.claude", "tom's Claude", "Claude Opus 4.5", TestData.Now, OwnerId, "tom", "Tom");
        store.GetAgentWithOwnerAsync(row.Id, Arg.Any<CancellationToken>()).Returns(row);

        var result = await CreateService().ConnectAsync(row.Id, "GPT-5", TestSupport.Ct);

        Assert.Equal("Claude Opus 4.5", result.Value.ModelName);
        await store.DidNotReceiveWithAnyArgs().TryConnectAsync(default, default!, default!, default!, TestSupport.Ct);
        Assert.Empty(unitOfWork.Locks);
    }

    [Fact]
    public async Task LosingTheRaceToConnectKeepsTheWinnersNames()
    {
        var row = new AgentWithOwnerRow(Guid.NewGuid(), null, null, null, TestData.Now, OwnerId, "tom", "Tom");
        store.GetAgentWithOwnerAsync(row.Id, Arg.Any<CancellationToken>()).Returns(row);
        store.ListHandlesStartingWithAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new HashSet<string>());

        var result = await CreateService().ConnectAsync(row.Id, "Claude", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.DidNotReceiveWithAnyArgs().ForgetKeysAsync(default!, TestSupport.Ct);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("bad\u0007name")]
    public async Task ConnectingNeedsAModelName(string? modelName)
    {
        var result = await CreateService().ConnectAsync(Guid.NewGuid(), modelName, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.InvalidInput, result.Error?.Code);
        Assert.Equal(["Validation.ModelName(100)"], result.Error!.Fields![AgentFields.ModelName]);
        await store.DidNotReceiveWithAnyArgs().GetAgentWithOwnerAsync(default, TestSupport.Ct);
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
            [new AgentRow(agent.Id, "tom.helper", "Helper", "Claude", TestData.Now, null, "beeps_abc", TestData.Now, null, "bio")]);

        var result = await CreateService().UpdateProfileAsync(OwnerId, agent.Id, new AgentProfileRequest(" Helper ", "helper", " I help.\r\nMore. "), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(("Helper", "tom.helper", "tom.helper", "I help.\nMore."), (agent.DisplayName, agent.Handle, agent.NormalizedHandle, agent.Bio));
        Assert.Equal([AdvisoryLocks.OwnerAgents(OwnerId)], unitOfWork.Locks);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal("helper", result.Value.HandleName);
    }

    [Fact]
    public async Task KeepingTheHandleDoesNotCheckIt()
    {
        var agent = ConnectedAgent();
        store.ListAgentsAsync(OwnerId, Arg.Any<CancellationToken>()).Returns([new AgentRow(agent.Id, "tom.claude", "x", "Claude", TestData.Now, null, null, null, null)]);

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
        TestData.Avatars,
        Options.Create(options),
        TestData.Time(),
        new TestLocalizer(),
        NullLogger<AgentService>.Instance);
}
