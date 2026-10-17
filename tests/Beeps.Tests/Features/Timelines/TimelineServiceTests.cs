using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Timelines;

public sealed class TimelineServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly PageRequest Page = PageRequest.Create(Guid.NewGuid(), Guid.NewGuid(), 10);

    private readonly ITimelineStore store = Substitute.For<ITimelineStore>();
    private readonly IProfileStore profiles = Substitute.For<IProfileStore>();
    private readonly IBeepStore beeps = Substitute.For<IBeepStore>();
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();
    private readonly BeepRow row;

    public TimelineServiceTests()
    {
        var id = Guid.CreateVersion7();
        row = new BeepRow(id, "hi #Beeps", new ActorRow(OwnerId, ActorKind.Human, "tom", "Tom", null), null, id, TestData.Now, 0, 0, false);
        var page = new CursorPage<BeepRow>([row], row.Id);
        store.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(page);
        store.HomeAsync(default, default!, default, TestSupport.Ct).ReturnsForAnyArgs(page);
        store.ProfileAsync(default, default, default!, default, TestSupport.Ct).ReturnsForAnyArgs(page);
        store.HashtagAsync(default!, default!, default, TestSupport.Ct).ReturnsForAnyArgs(page);
        beeps.GetMentionsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<ActorRow>>());
        counters.GetAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, BeepCounts>());
    }

    [Fact]
    public async Task ThePublicTimelineIsShownToAnyone()
    {
        var result = await Create().GetPublicAsync(null, Page, TestSupport.Ct);

        Assert.Equal(row.Id, Assert.Single(result.Items).Id);
        Assert.Equal(row.Id, result.NextCursor);
        await store.Received(1).PublicAsync(Page, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAgentReadsItsOwnersHomeTimeline()
    {
        var agent = Viewer.Agent(Guid.NewGuid(), OwnerId);

        var result = await Create().GetHomeAsync(agent, Page, TestSupport.Ct);

        Assert.Equal(Relations.Owner, Assert.Single(result.Items).Author.Relation);
        await store.Received(1).HomeAsync(OwnerId, Page, agent.ActorId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AHumanReadsTheirOwnHomeTimeline()
    {
        await Create().GetHomeAsync(Viewer.Human(OwnerId), Page, TestSupport.Ct);

        await store.Received(1).HomeAsync(OwnerId, Page, OwnerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AProfileAlreadyFoundIsListedWithItsTab()
    {
        var actorId = Guid.NewGuid();
        var viewer = Viewer.Human(OwnerId);

        var result = await Create().GetProfileAsync(viewer, actorId, ProfileTimeline.Beeps, Page, TestSupport.Ct);

        Assert.Single(result.Items);
        await store.Received(1).ProfileAsync(actorId, ProfileTimeline.Beeps, Page, OwnerId, Arg.Any<CancellationToken>());
        await profiles.DidNotReceiveWithAnyArgs().FindByHandleAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task AProfileTimelineNeedsAVisibleProfile()
    {
        var actor = new ActorRow(Guid.NewGuid(), ActorKind.Agent, "tom.claude", "tom's Claude", OwnerId);
        profiles.FindByHandleAsync("tom.claude", Arg.Any<CancellationToken>()).Returns(new ProfileRow(actor, null, "Claude", TestData.Now, null));
        var service = Create();

        var found = await service.GetProfileAsync(null, "@Tom.Claude", ProfileTimeline.Replies, Page, TestSupport.Ct);
        var missing = await service.GetProfileAsync(null, "nobody", ProfileTimeline.All, Page, TestSupport.Ct);

        Assert.True(found.IsSuccess);
        await store.Received(1).ProfileAsync(actor.Id, ProfileTimeline.Replies, Page, null, Arg.Any<CancellationToken>());
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
    }

    [Fact]
    public async Task HashtagsAreNormalizedAndChecked()
    {
        var service = Create();

        var found = await service.GetHashtagAsync(null, "#BEEPS", Page, TestSupport.Ct);
        var invalid = await service.GetHashtagAsync(null, "not a tag", Page, TestSupport.Ct);

        Assert.True(found.IsSuccess);
        await store.Received(1).HashtagAsync("beeps", Page, null, Arg.Any<CancellationToken>());
        Assert.Equal(BeepErrorCodes.InvalidHashtag, invalid.Error?.Code);
    }

    private TimelineService Create()
    {
        var views = new BeepViews(beeps, counters, TestData.Avatars, TestData.Media, NullLogger<BeepViews>.Instance);
        var profileService = new ProfileService(profiles, views, new ImmediateUnitOfWork(), TestData.AllowingRateLimits(), TestData.Live(Substitute.For<ILiveEventStore>()), TestData.Time(), new TestLocalizer(), NullLogger<ProfileService>.Instance);
        return new TimelineService(store, profileService, views, new TestLocalizer());
    }
}
