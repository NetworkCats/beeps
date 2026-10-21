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

public sealed class BannerServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();
    private static readonly Viewer Human = Viewer.Human(OwnerId);
    private static readonly Viewer Agent = Viewer.Agent(AgentId, OwnerId);

    private readonly IAvatarStore store = Substitute.For<IAvatarStore>();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();
    private readonly MediaServiceParts media = MediaServiceParts.WithRealImages();

    public BannerServiceTests()
    {
        store.FindActorAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(OwnerId, ActorKind.Human, null));
        store.FindActorAsync(AgentId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(AgentId, ActorKind.Agent, OwnerId));
    }

    [Fact]
    public async Task AHumanUploadsTheirOwnBanner()
    {
        Guid? uploaded = null;
        media.Store.When(s => s.AddMedia(Arg.Any<MediaItem>())).Do(ci => uploaded = ci.Arg<MediaItem>().Id);
        media.OwnEverything();

        var result = await Create().UploadAsync(Human, OwnerId, TestImages.Content(TestImages.Png(300, 100)), null, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).SetBannerAsync(OwnerId, uploaded, Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
        // An open profile page shows the new banner.
        Assert.Equal([$$"""{"actor_id":"{{OwnerId:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.ActorChanged));
    }

    [Fact]
    public async Task AHumanChangesTheirAgentsBanner()
    {
        media.OwnEverything();

        var result = await Create().UploadAsync(Human, AgentId, TestImages.Content(TestImages.Png(300, 100)), null, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).SetBannerAsync(AgentId, Arg.Is<Guid?>(id => id != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AgentsCannotChangeBannersNotEvenTheirOwnAndNobodyChangesStrangers()
    {
        media.OwnEverything();
        var strangerId = Guid.NewGuid();
        store.FindActorAsync(strangerId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(strangerId, ActorKind.Agent, Guid.NewGuid()));
        var service = Create();

        var own = await service.UploadAsync(Agent, AgentId, TestImages.Content(TestImages.Png(300, 100)), null, TestSupport.Ct);
        var removed = await service.RemoveAsync(Agent, AgentId, TestSupport.Ct);
        var strangers = await service.UploadAsync(Human, strangerId, TestImages.Content(TestImages.Png(300, 100)), null, TestSupport.Ct);
        var missing = await service.RemoveAsync(Human, Guid.NewGuid(), TestSupport.Ct);

        Assert.All([own, removed, strangers, missing], r => Assert.Equal(ErrorKind.NotFound, r.Error?.Kind));
        await media.Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default, default!, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().SetBannerAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task RemovingLeavesNoBanner()
    {
        var result = await Create().RemoveAsync(Human, OwnerId, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).SetBannerAsync(OwnerId, null, Arg.Any<CancellationToken>());
        await media.Store.DidNotReceiveWithAnyArgs().LockOwnedAsync(default, default!, default, TestSupport.Ct);
    }

    [Fact]
    public async Task ChangesCountAgainstTheBannerQuota()
    {
        var limits = Substitute.For<IRateLimitStore>();
        limits.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromMinutes(1)));

        var result = await Create(limits).RemoveAsync(Human, AgentId, TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        await limits.Received(1).TryAcquireAsync(Arg.Is<IReadOnlyList<RateLimitCounter>>(c => TestData.Policies(c).Single() == RateLimitPolicies.BannerChange), Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().SetBannerAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task TheUrlIsThatOfTheReadyBanner()
    {
        store.FindBannerKeyAsync(OwnerId, Arg.Any<CancellationToken>()).Returns("wide");
        store.FindBannerKeyAsync(AgentId, Arg.Any<CancellationToken>()).Returns((string?)null);

        Assert.Equal(TestData.Media.Banner("wide"), await Create().GetUrlAsync(OwnerId, TestSupport.Ct));
        Assert.Null(await Create().GetUrlAsync(AgentId, TestSupport.Ct));
    }

    [Fact]
    public async Task TheStripPickedInTheCropperBecomesTheBanner()
    {
        var banner = CaptureBanner();
        media.OwnEverything();

        // 400 × 200 in four quadrants; the 150 × 50 strip at (200, 100) lies inside the bottom-right quadrant.
        var result = await Create().UploadAsync(Human, OwnerId, TestImages.Content(TestImages.Quadrants(400, 200)), new BannerCrop(200, 100, 150), TestSupport.Ct);
        await media.ProcessQueuedAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal((150, 50), TestImages.SizeOf(banner()));
        Assert.True(TestImages.Near(TestImages.ColorAt(banner(), 0.5f, 0.5f), TestImages.BottomRight));
    }

    [Fact]
    public async Task WithoutACropTheWidestStripInTheMiddleIsUsed()
    {
        var banner = CaptureBanner();
        media.OwnEverything();

        await Create().UploadAsync(Human, OwnerId, TestImages.Content(TestImages.Quadrants(300, 300)), null, TestSupport.Ct);
        await media.ProcessQueuedAsync();

        // The whole width, a third of the height, straddling the middle.
        Assert.Equal((300, 100), TestImages.SizeOf(banner()));
        Assert.True(TestImages.Near(TestImages.ColorAt(banner(), 0.25f, 0.1f), TestImages.TopLeft));
        Assert.True(TestImages.Near(TestImages.ColorAt(banner(), 0.75f, 0.9f), TestImages.BottomRight));
    }

    [Fact]
    public async Task AWideImageIsStoredNoWiderThanTheBannerSize()
    {
        var banner = CaptureBanner();
        media.OwnEverything();

        await Create().UploadAsync(Human, OwnerId, TestImages.Content(TestImages.Quadrants(3000, 1000)), null, TestSupport.Ct);
        await media.ProcessQueuedAsync();

        Assert.Equal((MediaRules.BannerWidth, MediaRules.BannerWidth / MediaRules.BannerAspect), TestImages.SizeOf(banner()));
    }

    [Fact]
    public async Task ACropOutsideTheImageChangesNothing()
    {
        var result = await Create().UploadAsync(Human, OwnerId, TestImages.Content(TestImages.Quadrants(200, 100)), new BannerCrop(100, 0, 300), TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.BannerCropInvalid, result.Error?.Code);
        await media.Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default, default!, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().SetBannerAsync(default, default, TestSupport.Ct);
    }

    /// <summary>The bytes stored as the banner size of the next upload.</summary>
    private Func<byte[]> CaptureBanner()
    {
        byte[]? stored = null;
        media.Storage.When(s => s.PutAsync(Arg.Is<string>(n => n.EndsWith("/banner.webp", StringComparison.Ordinal)), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(ci => stored = ci.ArgAt<ReadOnlyMemory<byte>>(1).ToArray());
        return () => stored ?? throw new InvalidOperationException("No banner was stored.");
    }

    private BannerService Create(IRateLimitStore? limits = null)
    {
        var rateLimits = limits ?? Substitute.For<IRateLimitStore>();
        if (limits is null)
        {
            rateLimits.TryAcquireAsync(default!, CancellationToken.None).ReturnsForAnyArgs(RateLimitDecision.Allowed);
        }

        return new BannerService(
            store,
            media.Store,
            media.Create(),
            TestData.Media,
            media.UnitOfWork,
            TestData.RateLimits(rateLimits),
            TestData.Live(liveEvents),
            new TestLocalizer(),
            NullLogger<BannerService>.Instance);
    }
}
