using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Features.Live;

public sealed class LiveFeedServiceTests
{
    private static readonly Viewer Tom = Viewer.Human(Guid.NewGuid());
    private static readonly Guid Newest = Guid.CreateVersion7();

    private readonly ITimelineStore timelines = Substitute.For<ITimelineStore>();
    private readonly IBeepStore beeps = Substitute.For<IBeepStore>();
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();
    private readonly BeepRow row = TestBeeps.Row("new", TestBeeps.Human("alice"));
    private readonly CursorPage<BeepRow> page;

    public LiveFeedServiceTests()
    {
        page = new CursorPage<BeepRow>([row], null);
        timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(page);
        timelines.HomeAsync(default, default!, default, TestSupport.Ct).ReturnsForAnyArgs(page);
        timelines.ProfileAsync(default, default, default!, default, TestSupport.Ct).ReturnsForAnyArgs(page);
        timelines.HashtagAsync(default!, default!, default, TestSupport.Ct).ReturnsForAnyArgs(page);
        beeps.GetMentionsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<ActorRow>>());
        beeps.GetMediaAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<MediaRow>>());
        counters.GetAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, BeepCounts>());
    }

    [Fact]
    public async Task ListsAskTheirOwnQueryForWhatIsNewerThanTheNewestShown()
    {
        var profile = Guid.NewGuid();
        var service = Create();

        var home = await service.GetNewBeepsAsync(Tom, "home", Newest, TestSupport.Ct);
        await service.GetNewBeepsAsync(Tom, "public", Newest, TestSupport.Ct);
        await service.GetNewBeepsAsync(Tom, $"replies:{profile:D}", Newest, TestSupport.Ct);
        await service.GetNewBeepsAsync(Tom, "tag:Beeps", Newest, TestSupport.Ct);

        Assert.Equal([row.Id], home.Value.Items.Select(b => b.Id));
        Assert.False(home.Value.InThread);
        var since = PageRequest.Create(sinceId: Newest, limit: LiveFeedService.Limit);
        await timelines.Received(1).HomeAsync(Tom.ActorId, since, Tom.ActorId, Arg.Any<CancellationToken>());
        await timelines.Received(1).PublicAsync(since, Tom.ActorId, Arg.Any<CancellationToken>());
        await timelines.Received(1).ProfileAsync(profile, ProfileTimeline.Replies, since, Tom.ActorId, Arg.Any<CancellationToken>());
        await timelines.Received(1).HashtagAsync("beeps", since, Tom.ActorId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AThreadAddsTheRepliesAfterItsLastOneWithWhomTheyAnswer()
    {
        var root = TestBeeps.Row("root", TestBeeps.Human("tom"));
        var reply = TestBeeps.Row("reply", TestBeeps.Human("alice"), replyTo: root.Id, root: root.Id);
        beeps.ListThreadAsync(root.Id, Newest, LiveFeedService.Limit, Tom.ActorId, Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([reply], null));
        beeps.GetRowsAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == root.Id), Tom.ActorId, Arg.Any<CancellationToken>()).Returns([root]);

        var result = await Create().GetNewBeepsAsync(Tom, $"thread:{root.Id:D}", Newest, TestSupport.Ct);

        Assert.True(result.Value.InThread);
        Assert.Equal([reply.Id], result.Value.Items.Select(b => b.Id));
        Assert.Equal("tom", result.Value.ReplyingTo[root.Id]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("notifications")]
    [InlineData("nonsense")]
    public async Task OnlyPagesThatListBeepsHaveNewOnes(string watch)
    {
        var result = await Create().GetNewBeepsAsync(Tom, watch, Newest, TestSupport.Ct);

        Assert.Equal(LiveErrorCodes.InvalidWatch, result.Error?.Code);
    }

    private LiveFeedService Create()
    {
        var localizer = new TestLocalizer();
        var views = new BeepViews(beeps, counters, TestData.Avatars, TestData.Media, NullLogger<BeepViews>.Instance);
        var profiles = new ProfileService(Substitute.For<IProfileStore>(), views, new ImmediateUnitOfWork(), TestData.AllowingRateLimits(), TestData.Time(), localizer, NullLogger<ProfileService>.Instance);
        var media = new MediaServiceParts();
        var beepService = new BeepService(
            beeps,
            counters,
            media.Store,
            media.Create(),
            views,
            new ImmediateUnitOfWork(),
            Substitute.For<IOutbox>(),
            Substitute.For<IModerationJobs>(),
            TestData.RateLimits(media.RateLimits),
            TestData.Live(Substitute.For<ILiveEventStore>()),
            Options.Create(new ModerationOptions()),
            TestData.Time(),
            localizer,
            NullLogger<BeepService>.Instance);
        return new LiveFeedService(new TimelineService(timelines, profiles, views, localizer), beepService, localizer);
    }
}
