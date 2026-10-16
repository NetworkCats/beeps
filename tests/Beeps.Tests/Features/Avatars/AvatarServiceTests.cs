using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
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
    private readonly MediaServiceParts media = MediaServiceParts.WithRealImages();

    public AvatarServiceTests()
    {
        store.FindActorAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(OwnerId, ActorKind.Human, null));
        store.FindActorAsync(AgentId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(AgentId, ActorKind.Agent, OwnerId));
    }

    [Fact]
    public async Task AnAgentChangesItsOwnAvatarToAnImageItUploaded()
    {
        var mediaId = Guid.NewGuid();
        media.Store.LockOwnedAsync(AgentId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == mediaId), Arg.Any<CancellationToken>()).Returns([mediaId]);

        var result = await Create().SetAsync(Agent, AgentId, mediaId, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).SetAvatarAsync(AgentId, mediaId, Arg.Any<CancellationToken>());
        // Nothing caches an agent's avatar; a human's sits in the cached account state.
        await accounts.DidNotReceiveWithAnyArgs().ForgetAccountAsync(default, TestSupport.Ct);
    }

    [Fact]
    public async Task AHumanChangesTheirOwnAvatarAndTheCachedAccountIsDropped()
    {
        media.OwnEverything();

        var result = await Create().SetAsync(Human, OwnerId, Guid.NewGuid(), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await accounts.Received(1).ForgetAccountAsync(OwnerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AHumanChangesTheirAgentsAvatar()
    {
        media.OwnEverything();

        var result = await Create().SetAsync(Human, AgentId, Guid.NewGuid(), TestSupport.Ct);

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

        var owners = await service.SetAsync(Agent, OwnerId, Guid.NewGuid(), TestSupport.Ct);
        var strangers = await service.SetAsync(Human, strangerId, Guid.NewGuid(), TestSupport.Ct);
        var missing = await service.RemoveAsync(Human, Guid.NewGuid(), TestSupport.Ct);

        Assert.All([owners, strangers, missing], r => Assert.Equal(ErrorKind.NotFound, r.Error?.Kind));
        await store.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task OnlyImagesTheViewerUploadedCanBecomeAnAvatar()
    {
        media.Store.LockOwnedAsync(default, default!, TestSupport.Ct).ReturnsForAnyArgs([]);

        var result = await Create().SetAsync(Human, AgentId, Guid.NewGuid(), TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.NotFound, result.Error?.Code);
        Assert.Contains(MediaFields.MediaId, result.Error!.Fields!.Keys);
        await store.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task RemovingGoesBackToTheDefaultPattern()
    {
        var result = await Create().RemoveAsync(Human, OwnerId, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).SetAvatarAsync(OwnerId, null, Arg.Any<CancellationToken>());
        await media.Store.DidNotReceiveWithAnyArgs().LockOwnedAsync(default, default!, TestSupport.Ct);
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
            new TestLocalizer(),
            NullLogger<AvatarService>.Instance);
    }
}
