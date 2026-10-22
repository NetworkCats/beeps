using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Avatars;

public sealed class AvatarServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();
    private static readonly Viewer Human = Viewer.Human(OwnerId);
    private static readonly Viewer Agent = Viewer.Agent(AgentId, OwnerId);

    private readonly IAvatarStore store = Substitute.For<IAvatarStore>();
    private readonly IAccountStore accounts = Substitute.For<IAccountStore>();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();
    private readonly MediaServiceParts media = MediaServiceParts.WithRealImages();

    public AvatarServiceTests()
    {
        store.FindActorAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(OwnerId, ActorKind.Human, null));
        store.FindActorAsync(AgentId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(AgentId, ActorKind.Agent, OwnerId));
    }

    [Fact]
    public async Task AnAgentUploadsItsOwnAvatar()
    {
        MediaItem? uploaded = null;
        media.Store.When(s => s.AddMedia(Arg.Any<MediaItem>())).Do(ci => uploaded = ci.Arg<MediaItem>());
        media.OwnEverything();

        var result = await Create().UploadAsync(Agent, AgentId, TestImages.Content(TestImages.Png()), null, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaPurpose.Avatar, uploaded?.Purpose);
        await store.Received(1).SetAvatarAsync(AgentId, uploaded!.Id, Arg.Any<CancellationToken>());
        // Only an image uploaded as an avatar can be one.
        await media.Store.Received(1).LockOwnedAsync(AgentId, Arg.Any<IReadOnlyCollection<Guid>>(), MediaPurpose.Avatar, Arg.Any<CancellationToken>());
        // Nothing caches an agent's avatar; a human's sits in the cached account state.
        await accounts.DidNotReceiveWithAnyArgs().ForgetAccountAsync(default, TestSupport.Ct);
    }

    [Fact]
    public async Task AHumanChangesTheirOwnAvatarAndTheCachedAccountIsDropped()
    {
        media.OwnEverything();

        var result = await Create().UploadAsync(Human, OwnerId, TestImages.Content(TestImages.Png()), null, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await accounts.Received(1).ForgetAccountAsync(OwnerId, Arg.Any<CancellationToken>());
        // An open profile page shows the new avatar.
        Assert.Equal([$$"""{"actor_id":"{{OwnerId:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.ActorChanged));
    }

    [Fact]
    public async Task AHumanChangesTheirAgentsAvatar()
    {
        media.OwnEverything();

        var result = await Create().UploadAsync(Human, AgentId, TestImages.Content(TestImages.Png()), null, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).SetAvatarAsync(AgentId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAgentCannotChangeItsOwnersAvatarAndNobodyChangesStrangers()
    {
        media.OwnEverything();
        var strangerId = Guid.NewGuid();
        store.FindActorAsync(strangerId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(strangerId, ActorKind.Agent, Guid.NewGuid()));
        var service = Create();

        var owners = await service.UploadAsync(Agent, OwnerId, TestImages.Content(TestImages.Png()), null, TestSupport.Ct);
        var strangers = await service.UploadAsync(Human, strangerId, TestImages.Content(TestImages.Png()), null, TestSupport.Ct);
        var missing = await service.RemoveAsync(Human, Guid.NewGuid(), TestSupport.Ct);

        Assert.All([owners, strangers, missing], r => Assert.Equal(ErrorKind.NotFound, r.Error?.Kind));
        await store.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task AnUploadThatAlreadyFailedToDecodeIsNotSet()
    {
        media.Store.LockOwnedAsync(default, default!, default, TestSupport.Ct).ReturnsForAnyArgs([]);

        var result = await Create().UploadAsync(Human, AgentId, TestImages.Content(TestImages.Png()), null, TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.Unreadable, result.Error?.Code);
        await store.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task AnAvatarIsStoredInTheAvatarSizeAlone()
    {
        media.OwnEverything();

        await Create().UploadAsync(Human, OwnerId, TestImages.Content(TestImages.Png()), null, TestSupport.Ct);
        await media.ProcessQueuedAsync();

        var stored = media.Storage.ReceivedCalls().Select(c => (string)c.GetArguments()[0]!).ToList();
        Assert.Single(stored, n => n.EndsWith("/avatar.webp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemovingGoesBackToTheDefaultPattern()
    {
        var result = await Create().RemoveAsync(Human, OwnerId, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).SetAvatarAsync(OwnerId, null, Arg.Any<CancellationToken>());
        await media.Store.DidNotReceiveWithAnyArgs().LockOwnedAsync(default, default!, default, TestSupport.Ct);
        await accounts.Received(1).ForgetAccountAsync(OwnerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangesCountAgainstTheAvatarQuota()
    {
        var limits = Substitute.For<IRateLimitStore>();
        limits.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromMinutes(1)));

        var result = await Create(limits).RemoveAsync(Agent, AgentId, TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        await limits.Received(1).TryAcquireAsync(Arg.Is<IReadOnlyList<RateLimitCounter>>(c => TestData.Policies(c).Single() == RateLimitPolicies.AvatarChange), Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task UploadingFromTheWebStoresTheImageAndUsesIt()
    {
        Guid? uploaded = null;
        media.Store.When(s => s.AddMedia(Arg.Any<MediaItem>())).Do(ci => uploaded = ci.Arg<MediaItem>().Id);
        media.OwnEverything();

        var result = await Create().UploadAsync(Human, AgentId, TestImages.Content(TestImages.Png()), null, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).SetAvatarAsync(AgentId, uploaded, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadingForSomeoneElseUploadsNothing()
    {
        var result = await Create().UploadAsync(Agent, OwnerId, TestImages.Content(TestImages.Png()), null, TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
        await media.Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task ABadUploadChangesNothing()
    {
        var result = await Create().UploadAsync(Human, OwnerId, TestImages.Content("nope"u8.ToArray()), null, TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.TypeUnsupported, result.Error?.Code);
        await store.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task TheSquarePickedInTheCropperBecomesTheAvatar()
    {
        var avatar = CaptureAvatar();
        media.OwnEverything();

        // 200 × 100 in four quadrants; the 50-pixel square at (100, 0) is the whole top-right quadrant.
        var result = await Create().UploadAsync(Human, OwnerId, TestImages.Content(TestImages.Quadrants(200, 100)), new AvatarCrop(100, 0, 50), TestSupport.Ct);
        await media.ProcessQueuedAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal((50, 50), TestImages.SizeOf(avatar()));
        Assert.True(TestImages.Near(TestImages.ColorAt(avatar(), 0.5f, 0.5f), TestImages.TopRight));
    }

    [Fact]
    public async Task WithoutACropTheCenterIsUsed()
    {
        var avatar = CaptureAvatar();
        media.OwnEverything();

        await Create().UploadAsync(Human, OwnerId, TestImages.Content(TestImages.Quadrants(300, 100)), null, TestSupport.Ct);
        await media.ProcessQueuedAsync();

        // The centered 100-pixel square straddles the middle: left half from the left quadrants.
        Assert.True(TestImages.Near(TestImages.ColorAt(avatar(), 0.25f, 0.25f), TestImages.TopLeft));
        Assert.True(TestImages.Near(TestImages.ColorAt(avatar(), 0.75f, 0.75f), TestImages.BottomRight));
    }

    [Fact]
    public async Task ACropOutsideTheImageChangesNothing()
    {
        var result = await Create().UploadAsync(Human, OwnerId, TestImages.Content(TestImages.Quadrants(200, 100)), new AvatarCrop(150, 0, 100), TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.AvatarCropInvalid, result.Error?.Code);
        await media.Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default, default!, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task AnImportedImageBecomesTheFirstAvatarWithoutUsingTheAvatarQuota()
    {
        Guid? uploaded = null;
        media.Store.When(s => s.AddMedia(Arg.Any<MediaItem>())).Do(ci => uploaded = ci.Arg<MediaItem>().Id);
        media.OwnEverything();
        store.SetFirstAvatarAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs(true);
        var limits = Substitute.For<IRateLimitStore>();
        limits.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Allowed);

        var result = await Create(limits).ImportAsync(OwnerId, TestImages.Content(TestImages.Png()), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).SetFirstAvatarAsync(OwnerId, uploaded!.Value, Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
        await accounts.Received(1).ForgetAccountAsync(OwnerId, Arg.Any<CancellationToken>());
        Assert.Equal([$$"""{"actor_id":"{{OwnerId:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.ActorChanged));
        // The upload counts as theirs; the change is not one they made.
        await limits.DidNotReceive().TryAcquireAsync(Arg.Is<IReadOnlyList<RateLimitCounter>>(c => TestData.Policies(c).Contains(RateLimitPolicies.AvatarChange)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnImportedImageLeavesAnAvatarSetMeanwhile()
    {
        media.OwnEverything();
        store.SetFirstAvatarAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs(false);

        var result = await Create().ImportAsync(OwnerId, TestImages.Content(TestImages.Png()), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
        await accounts.DidNotReceiveWithAnyArgs().ForgetAccountAsync(default, TestSupport.Ct);
        Assert.Empty(TestData.LiveEvents(liveEvents, LiveEventTypes.ActorChanged));
    }

    [Fact]
    public async Task OnlyHumansGetAnImportedAvatar()
    {
        var result = await Create().ImportAsync(AgentId, TestImages.Content(TestImages.Png()), TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
        media.Store.DidNotReceiveWithAnyArgs().AddMedia(default!);
    }

    /// <summary>The bytes stored as the avatar size of the next upload.</summary>
    private Func<byte[]> CaptureAvatar()
    {
        byte[]? stored = null;
        media.Storage.When(s => s.PutAsync(Arg.Is<string>(n => n.EndsWith("/avatar.webp", StringComparison.Ordinal)), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(ci => stored = ci.ArgAt<ReadOnlyMemory<byte>>(1).ToArray());
        return () => stored ?? throw new InvalidOperationException("No avatar was stored.");
    }

    private AvatarService Create(IRateLimitStore? limits = null)
    {
        var rateLimits = limits ?? Substitute.For<IRateLimitStore>();
        if (limits is null)
        {
            rateLimits.TryAcquireAsync(default!, CancellationToken.None).ReturnsForAnyArgs(RateLimitDecision.Allowed);
        }

        return new AvatarService(
            store,
            media.Store,
            media.Create(),
            accounts,
            media.UnitOfWork,
            TestData.RateLimits(rateLimits),
            TestData.Live(liveEvents),
            new TestLocalizer(),
            NullLogger<AvatarService>.Instance);
    }
}
