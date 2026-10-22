using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.RateLimiting;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Beeps.Tests.Features.Avatars;

public sealed class GravatarImportJobTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly IAvatarStore store = Substitute.For<IAvatarStore>();
    private readonly IGravatar gravatar = Substitute.For<IGravatar>();
    private readonly IAccountStore accounts = Substitute.For<IAccountStore>();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();
    private readonly MediaServiceParts media = MediaServiceParts.WithRealImages();
    private readonly GravatarOptions options = new();

    public GravatarImportJobTests()
    {
        store.FindActorAsync(UserId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(UserId, ActorKind.Human, null));
        store.SetFirstAvatarAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs(true);
        media.OwnEverything();
    }

    [Fact]
    public async Task AnAccountWithAGravatarImageGetsItAsItsAvatar()
    {
        Checks(new GravatarCheck(UserId, "someone@example.com", NeedsAvatar: true));
        gravatar.FindAsync("someone@example.com", Arg.Any<CancellationToken>()).Returns(TestImages.Png());

        await CreateJob().RunAsync(TestSupport.Ct);

        await store.Received(1).SetFirstAvatarAsync(UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await store.Received(1).MarkGravatarCheckedAsync(UserId, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAccountWithoutAGravatarImageKeepsThePattern()
    {
        Checks(new GravatarCheck(UserId, "someone@example.com", NeedsAvatar: true));
        gravatar.FindAsync(default!, TestSupport.Ct).ReturnsForAnyArgs((byte[]?)null);

        await CreateJob().RunAsync(TestSupport.Ct);

        media.Store.DidNotReceiveWithAnyArgs().AddMedia(default!);
        await store.Received(1).MarkGravatarCheckedAsync(UserId, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAccountThatHasAnAvatarIsOnlyMarked()
    {
        Checks(new GravatarCheck(UserId, "someone@example.com", NeedsAvatar: false));

        await CreateJob().RunAsync(TestSupport.Ct);

        await gravatar.DidNotReceiveWithAnyArgs().FindAsync(default!, TestSupport.Ct);
        await store.Received(1).MarkGravatarCheckedAsync(UserId, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnImageTheUploadsRefuseIsNotAskedForAgain()
    {
        Checks(new GravatarCheck(UserId, "someone@example.com", NeedsAvatar: true));
        gravatar.FindAsync(default!, TestSupport.Ct).ReturnsForAnyArgs("not an image"u8.ToArray());

        await CreateJob().RunAsync(TestSupport.Ct);

        await store.DidNotReceiveWithAnyArgs().SetFirstAvatarAsync(default, default, TestSupport.Ct);
        await store.Received(1).MarkGravatarCheckedAsync(UserId, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhileGravatarIsAwayTheAccountIsAskedAgainNextRun()
    {
        var later = Guid.NewGuid();
        Checks(new GravatarCheck(UserId, "someone@example.com", NeedsAvatar: true), new GravatarCheck(later, "later@example.com", NeedsAvatar: true));
        gravatar.FindAsync(default!, TestSupport.Ct).ThrowsAsyncForAnyArgs(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateJob().RunAsync(TestSupport.Ct));

        await store.DidNotReceiveWithAnyArgs().MarkGravatarCheckedAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task SwitchedOffNobodyIsLookedUp()
    {
        options.Enabled = false;

        await CreateJob().RunAsync(TestSupport.Ct);

        await store.DidNotReceiveWithAnyArgs().ListGravatarChecksAsync(default, TestSupport.Ct);
    }

    private void Checks(params GravatarCheck[] checks) =>
        store.ListGravatarChecksAsync(GravatarImportJob.BatchSize, Arg.Any<CancellationToken>()).Returns(checks);

    private GravatarImportJob CreateJob()
    {
        var rateLimits = Substitute.For<IRateLimitStore>();
        rateLimits.TryAcquireAsync(default!, CancellationToken.None).ReturnsForAnyArgs(RateLimitDecision.Allowed);
        var avatars = new AvatarService(
            store,
            media.Store,
            media.Create(),
            accounts,
            media.UnitOfWork,
            TestData.RateLimits(rateLimits),
            TestData.Live(liveEvents),
            new TestLocalizer(),
            NullLogger<AvatarService>.Instance);
        return new GravatarImportJob(store, gravatar, avatars, Options.Create(options), TestData.Time(), NullLogger<GravatarImportJob>.Instance);
    }
}
