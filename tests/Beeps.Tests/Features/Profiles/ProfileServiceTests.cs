using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Profiles;

public sealed class ProfileServiceTests
{
    private static readonly ActorRow Tom = new(Guid.NewGuid(), ActorKind.Human, "tom", "Tom", null);
    private static readonly ActorRow TomsAgent = new(Guid.NewGuid(), ActorKind.Agent, "tom.claude", "tom's Claude", Tom.Id);
    private static readonly ActorRow Alice = new(Guid.NewGuid(), ActorKind.Human, "alice", "Alice", null);

    private readonly IProfileStore store = Substitute.For<IProfileStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly IRateLimitStore rateLimitStore = Substitute.For<IRateLimitStore>();
    private readonly ILiveEventStore liveStore = Substitute.For<ILiveEventStore>();

    public ProfileServiceTests()
    {
        rateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Allowed);
        store.FindByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(new ProfileRow(Tom, "hi", null, TestData.Now, null));
        store.FindByHandleAsync("tom.claude", Arg.Any<CancellationToken>()).Returns(new ProfileRow(TomsAgent, null, "Claude Opus 4.5", TestData.Now, Tom, BannerKey: "wide"));
        store.FindByHandleAsync("alice", Arg.Any<CancellationToken>()).Returns(new ProfileRow(Alice, null, null, TestData.Now, null));
        store.ListAgentsAsync(Tom.Id, Arg.Any<CancellationToken>()).Returns([TomsAgent]);
        store.GetPrivacyAsync(default, TestSupport.Ct).ReturnsForAnyArgs(ProfilePrivacy.None);
        store.CountFollowsAsync(default, TestSupport.Ct).ReturnsForAnyArgs(new FollowCounts(2, 3));
        store.GetRelationAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs(ProfileRelation.None);
    }

    [Fact]
    public async Task AHumansProfileListsTheirAgents()
    {
        store.GetRelationAsync(Alice.Id, Tom.Id, Arg.Any<CancellationToken>()).Returns(new ProfileRelation(Following: true, Blocking: false));

        var result = await Create().GetAsync(Viewer.Human(Alice.Id), "@TOM", TestSupport.Ct);

        Assert.Equal("hi", result.Value.Bio);
        Assert.Equal(TomsAgent.Id, Assert.Single(result.Value.Agents).Id);
        Assert.Null(result.Value.Owner);
        Assert.True(result.Value.Following);
    }

    [Fact]
    public async Task AHumansProfileCountsTheirFollowsAndShowsTheirLikesAndLists()
    {
        var result = await Create().GetAsync(null, "tom", TestSupport.Ct);

        Assert.Equal(new FollowCounts(2, 3), result.Value.Counts);
        Assert.True(result.Value.LikesShown);
        Assert.True(result.Value.FollowsShown);
        Assert.False(result.Value.Mine);
    }

    [Fact]
    public async Task WhatAHumanKeepsPrivateIsHiddenFromOthersButNotFromThem()
    {
        store.GetPrivacyAsync(Tom.Id, Arg.Any<CancellationToken>()).Returns(new ProfilePrivacy(true, true, true));
        var service = Create();

        var others = await service.GetAsync(Viewer.Human(Alice.Id), "tom", TestSupport.Ct);
        var signedOut = await service.GetAsync(null, "tom", TestSupport.Ct);
        var own = await service.GetAsync(Viewer.Human(Tom.Id), "tom", TestSupport.Ct);

        Assert.All([others.Value, signedOut.Value], profile =>
        {
            Assert.Null(profile.Counts);
            Assert.False(profile.LikesShown);
            Assert.False(profile.FollowsShown);
        });
        Assert.True(own.Value.Mine);
        Assert.Equal(new FollowCounts(2, 3), own.Value.Counts);
        Assert.True(own.Value.LikesShown);
        Assert.True(own.Value.FollowsShown);
        // Nothing is private from them, so their settings are not even read.
        await store.Received(2).GetPrivacyAsync(Tom.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EachSettingHidesOnlyWhatItSays()
    {
        store.GetPrivacyAsync(Tom.Id, Arg.Any<CancellationToken>()).Returns(new ProfilePrivacy(false, false, true), new ProfilePrivacy(false, true, false));
        var service = Create();

        var noCounts = await service.GetAsync(null, "tom", TestSupport.Ct);
        var noLists = await service.GetAsync(null, "tom", TestSupport.Ct);

        Assert.Equal((null, true, true), (noCounts.Value.Counts, noCounts.Value.LikesShown, noCounts.Value.FollowsShown));
        Assert.Equal((new FollowCounts(2, 3), true, false), (noLists.Value.Counts, noLists.Value.LikesShown, noLists.Value.FollowsShown));
    }

    [Fact]
    public async Task AnAgentKeepsItsLikesAsItsOwnerDoesAndHasNoFollows()
    {
        store.GetPrivacyAsync(Tom.Id, Arg.Any<CancellationToken>()).Returns(new ProfilePrivacy(true, false, false));
        var service = Create();

        var others = await service.GetAsync(Viewer.Human(Alice.Id), "tom.claude", TestSupport.Ct);
        var owners = await service.GetAsync(Viewer.Human(Tom.Id), "tom.claude", TestSupport.Ct);

        Assert.False(others.Value.LikesShown);
        Assert.True(owners.Value.Mine);
        Assert.True(owners.Value.LikesShown);
        Assert.All([others.Value, owners.Value], profile => Assert.Equal((null, false), (profile.Counts, profile.FollowsShown)));
        await store.DidNotReceiveWithAnyArgs().CountFollowsAsync(default, TestSupport.Ct);
    }

    [Fact]
    public async Task FollowListsAreShownAsTheViewerSeesThePeople()
    {
        var page = PageRequest.Create(Guid.NewGuid());
        store.ListFollowsAsync(Tom.Id, FollowList.Followers, page, Alice.Id, Arg.Any<CancellationToken>())
            .Returns(new CursorPage<PersonRow>([new PersonRow(Alice, "hello")], Alice.Id));

        var result = await Create().GetFollowsAsync(Viewer.Human(Alice.Id), Tom.Id, FollowList.Followers, page, TestSupport.Ct);

        var person = Assert.Single(result.Items);
        Assert.Equal((Alice.Id, Relations.Self, "hello"), (person.Actor.Id, person.Actor.Relation, person.Bio));
        Assert.Equal(Alice.Id, result.NextCursor);
    }

    [Fact]
    public async Task AnAgentsProfileNamesItsOwner()
    {
        var self = Viewer.Agent(TomsAgent.Id, Tom.Id);

        var result = await Create().GetAsync(self, "tom.claude", TestSupport.Ct);

        Assert.Equal(Relations.Self, result.Value.Actor.Relation);
        Assert.Equal(Relations.Owner, result.Value.Owner?.Relation);
        Assert.Equal("Claude Opus 4.5", result.Value.ModelName);
        Assert.Empty(result.Value.Agents);
        Assert.Null(result.Value.Following);
        await store.DidNotReceiveWithAnyArgs().GetRelationAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task AProfileShowsItsBannerOnTheMediaDomain()
    {
        var withBanner = await Create().GetAsync(null, "tom.claude", TestSupport.Ct);
        var without = await Create().GetAsync(null, "tom", TestSupport.Ct);

        Assert.Equal(new Uri("https://media.beeps.test/wide/banner.webp"), withBanner.Value.BannerUrl);
        Assert.Null(without.Value.BannerUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@")]
    [InlineData("nobody")]
    public async Task UnknownProfilesAreNotFound(string? handle)
    {
        var result = await Create().GetAsync(null, handle, TestSupport.Ct);
        var actor = await Create().FindActorAsync(handle, TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
        Assert.Equal(ErrorKind.NotFound, actor.Error?.Kind);
    }

    [Theory]
    [InlineData("wp-login.php")]
    [InlineData("tom.claude.extra")]
    [InlineData("汤姆")]
    public async Task UrlsThatCannotBeHandlesNeverReachTheDatabase(string handle)
    {
        var result = await Create().GetAsync(null, handle, TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
        await store.DidNotReceiveWithAnyArgs().FindByHandleAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task FindingAnActorReturnsIt()
    {
        var result = await Create().FindActorAsync("tom", TestSupport.Ct);

        Assert.Equal(Tom, result.Value);
    }

    [Fact]
    public async Task HumansFollowHumans()
    {
        store.AddFollowAsync(Alice.Id, Tom.Id, TestData.Now, Arg.Any<CancellationToken>()).Returns(true);
        var service = Create();

        var follow = await service.FollowAsync(Viewer.Human(Alice.Id), "tom", TestSupport.Ct);
        var unfollow = await service.UnfollowAsync(Viewer.Human(Alice.Id), "tom", TestSupport.Ct);

        Assert.True(follow.IsSuccess);
        Assert.True(unfollow.IsSuccess);
        await store.Received(1).AddFollowAsync(Alice.Id, Tom.Id, TestData.Now, Arg.Any<CancellationToken>());
        await store.Received(1).RemoveFollowAsync(Alice.Id, Tom.Id, Arg.Any<CancellationToken>());
        // Each in a transaction, which the cached list of whom Alice follows is invalidated with.
        Assert.Equal([null, null], unitOfWork.Locks);
    }

    [Fact]
    public async Task FollowingHasRules()
    {
        var service = Create();

        var byAgent = await service.FollowAsync(Viewer.Agent(TomsAgent.Id, Tom.Id), "alice", TestSupport.Ct);
        var ofAgent = await service.FollowAsync(Viewer.Human(Alice.Id), "tom.claude", TestSupport.Ct);
        var ofSelf = await service.FollowAsync(Viewer.Human(Alice.Id), "alice", TestSupport.Ct);
        var ofNobody = await service.UnfollowAsync(Viewer.Human(Alice.Id), "nobody", TestSupport.Ct);

        Assert.Equal(ProfileErrorCodes.AgentsCannotFollow, byAgent.Error?.Code);
        Assert.Equal(ProfileErrorCodes.CanOnlyFollowHumans, ofAgent.Error?.Code);
        Assert.Equal(ProfileErrorCodes.CannotFollowSelf, ofSelf.Error?.Code);
        Assert.Equal(ErrorKind.NotFound, ofNobody.Error?.Kind);
        await store.DidNotReceiveWithAnyArgs().AddFollowAsync(default, default, default, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().RemoveFollowAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task AProfileSaysWhetherTheViewingHumanBlockedIt()
    {
        store.GetRelationAsync(Alice.Id, TomsAgent.Id, Arg.Any<CancellationToken>()).Returns(new ProfileRelation(Following: false, Blocking: true));
        var service = Create();

        var blocked = await service.GetAsync(Viewer.Human(Alice.Id), "tom.claude", TestSupport.Ct);
        var own = await service.GetAsync(Viewer.Human(Tom.Id), "tom.claude", TestSupport.Ct);
        var signedOut = await service.GetAsync(null, "tom.claude", TestSupport.Ct);

        Assert.True(blocked.Value.Blocked);
        Assert.Null(own.Value.Blocked);
        Assert.Null(signedOut.Value.Blocked);
    }

    [Fact]
    public async Task BlockingAHumanEndsFollowingBothWays()
    {
        store.AddBlockAsync(Alice.Id, Tom.Id, TestData.Now, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Create().BlockAsync(Viewer.Human(Alice.Id), "@tom", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).AddBlockAsync(Alice.Id, Tom.Id, TestData.Now, Arg.Any<CancellationToken>());
        await store.Received(1).RemoveFollowAsync(Alice.Id, Tom.Id, Arg.Any<CancellationToken>());
        await store.Received(1).RemoveFollowAsync(Tom.Id, Alice.Id, Arg.Any<CancellationToken>());
        Assert.Equal([null], unitOfWork.Locks);
    }

    [Fact]
    public async Task BlockingAnAgentLeavesFollowingAlone()
    {
        var result = await Create().BlockAsync(Viewer.Human(Alice.Id), "tom.claude", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).AddBlockAsync(Alice.Id, TomsAgent.Id, TestData.Now, Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().RemoveFollowAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task NobodyBlocksThemselvesTheirAgentsOrAsAnAgent()
    {
        var service = Create();

        var self = await service.BlockAsync(Viewer.Human(Tom.Id), "tom", TestSupport.Ct);
        var ownAgent = await service.BlockAsync(Viewer.Human(Tom.Id), "tom.claude", TestSupport.Ct);
        var asAgent = await service.BlockAsync(Viewer.Agent(TomsAgent.Id, Tom.Id), "alice", TestSupport.Ct);
        var missing = await service.UnblockAsync(Viewer.Human(Tom.Id), "nobody", TestSupport.Ct);

        Assert.Equal(ProfileErrorCodes.CannotBlockOwn, self.Error?.Code);
        Assert.Equal(ProfileErrorCodes.CannotBlockOwn, ownAgent.Error?.Code);
        Assert.Equal(ProfileErrorCodes.AgentsCannotBlock, asAgent.Error?.Code);
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
        await store.DidNotReceiveWithAnyArgs().AddBlockAsync(default, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task UnblockingRemovesTheBlock()
    {
        var result = await Create().UnblockAsync(Viewer.Human(Alice.Id), "tom", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).RemoveBlockAsync(Alice.Id, Tom.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NobodyFollowsAcrossABlock()
    {
        store.AnyBlockBetweenAsync(Alice.Id, Tom.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Create().FollowAsync(Viewer.Human(Alice.Id), "tom", TestSupport.Ct);

        Assert.Equal((ErrorKind.Forbidden, ProfileErrorCodes.Blocked), (result.Error?.Kind, result.Error?.Code));
        await store.DidNotReceiveWithAnyArgs().AddFollowAsync(default, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task EachChangedFollowOrBlockTellsBothSidesLive()
    {
        store.AddFollowAsync(Alice.Id, Tom.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true, false);
        store.RemoveFollowAsync(Alice.Id, Tom.Id, Arg.Any<CancellationToken>()).Returns(true);
        store.AddBlockAsync(Alice.Id, Tom.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        store.RemoveBlockAsync(Alice.Id, Tom.Id, Arg.Any<CancellationToken>()).Returns(false);
        var (service, alice) = (Create(), Viewer.Human(Alice.Id));

        await service.FollowAsync(alice, "tom", TestSupport.Ct);
        await service.FollowAsync(alice, "tom", TestSupport.Ct);
        await service.UnfollowAsync(alice, "tom", TestSupport.Ct);
        await service.BlockAsync(alice, "tom", TestSupport.Ct);
        await service.UnblockAsync(alice, "tom", TestSupport.Ct);

        // The second follow and the unblock changed nothing.
        var both = $$"""{"actor_ids":["{{Alice.Id:D}}","{{Tom.Id:D}}"]}""";
        Assert.Equal([both, both, both], TestData.LiveEvents(liveStore, LiveEventTypes.RelationChanged));
    }

    [Fact]
    public async Task FollowsAndBlocksCountAgainstTheirOwnQuota()
    {
        rateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromMinutes(1)));
        var service = Create();
        var alice = Viewer.Human(Alice.Id);

        Result[] results =
        [
            await service.FollowAsync(alice, "tom", TestSupport.Ct),
            await service.UnfollowAsync(alice, "tom", TestSupport.Ct),
            await service.BlockAsync(alice, "tom", TestSupport.Ct),
            await service.UnblockAsync(alice, "tom", TestSupport.Ct),
        ];

        Assert.All(results, r => Assert.Equal(ErrorKind.RateLimited, r.Error?.Kind));
        await rateLimitStore.Received(4).TryAcquireAsync(
            Arg.Is<IReadOnlyList<RateLimitCounter>>(c => TestData.Policies(c).Single() == RateLimitPolicies.Follow), Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().AddFollowAsync(default, default, default, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().RemoveFollowAsync(default, default, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().AddBlockAsync(default, default, default, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().RemoveBlockAsync(default, default, TestSupport.Ct);
    }

    private ProfileService Create() => TestServices.Profiles(
        store,
        TestServices.Views(Substitute.For<IBeepStore>(), Substitute.For<IBeepCounterStore>()),
        unitOfWork,
        TestData.RateLimits(rateLimitStore),
        liveStore);
}
