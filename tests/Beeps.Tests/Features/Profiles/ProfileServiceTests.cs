using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Profiles;

public sealed class ProfileServiceTests
{
    private static readonly ActorRow Tom = new(Guid.NewGuid(), ActorKind.Human, "tom", "Tom", null);
    private static readonly ActorRow TomsAgent = new(Guid.NewGuid(), ActorKind.Agent, "tom.claude", "tom's Claude", Tom.Id);
    private static readonly ActorRow Alice = new(Guid.NewGuid(), ActorKind.Human, "alice", "Alice", null);

    private readonly IProfileStore store = Substitute.For<IProfileStore>();

    public ProfileServiceTests()
    {
        store.FindByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(new ProfileRow(Tom, "hi", null, TestData.Now, null));
        store.FindByHandleAsync("tom.claude", Arg.Any<CancellationToken>()).Returns(new ProfileRow(TomsAgent, null, "Claude Opus 4.5", TestData.Now, Tom));
        store.FindByHandleAsync("alice", Arg.Any<CancellationToken>()).Returns(new ProfileRow(Alice, null, null, TestData.Now, null));
        store.ListAgentsAsync(Tom.Id, Arg.Any<CancellationToken>()).Returns([TomsAgent]);
    }

    [Fact]
    public async Task AHumansProfileListsTheirAgents()
    {
        store.IsFollowingAsync(Alice.Id, Tom.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Create().GetAsync(Viewer.Human(Alice.Id), "@TOM", TestSupport.Ct);

        Assert.Equal("hi", result.Value.Bio);
        Assert.Equal(TomsAgent.Id, Assert.Single(result.Value.Agents).Id);
        Assert.Null(result.Value.Owner);
        Assert.True(result.Value.Following);
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
        await store.DidNotReceiveWithAnyArgs().IsFollowingAsync(default, default, TestSupport.Ct);
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

    private ProfileService Create()
    {
        var views = new BeepViews(
            Substitute.For<IBeepStore>(), Substitute.For<IBeepCounterStore>(), TestData.Avatars, TestData.Media, NullLogger<BeepViews>.Instance);
        return new ProfileService(store, views, TestData.Time(), new TestLocalizer(), NullLogger<ProfileService>.Instance);
    }
}
