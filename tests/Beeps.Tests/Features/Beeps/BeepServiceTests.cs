using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Notifications;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Features.Beeps;

public sealed class BeepServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Viewer Human = Viewer.Human(OwnerId);
    private static readonly Viewer Agent = Viewer.Agent(Guid.NewGuid(), OwnerId);

    private readonly IBeepStore store = Substitute.For<IBeepStore>();
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();
    private readonly IOutbox outbox = Substitute.For<IOutbox>();
    private readonly IRateLimitStore rateLimitStore = Substitute.For<IRateLimitStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly MediaServiceParts media;
    private readonly List<string[]> checkedPolicies = [];
    private Beep? added;
    private RateLimitDecision decision = RateLimitDecision.Allowed;
    private bool moderationEnabled = true;

    public BeepServiceTests()
    {
        media = new MediaServiceParts { UnitOfWork = unitOfWork };
        rateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(ci =>
        {
            checkedPolicies.Add(TestData.Policies(ci.Arg<IReadOnlyList<RateLimitCounter>>()));
            return decision;
        });
        store.When(s => s.AddBeep(Arg.Any<Beep>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>()))
            .Do(ci => added = ci.Arg<Beep>());
        // After a post, the service reads the new beep back.
        store.GetRowsAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(ci =>
            added is null ? [] : [Row(added.Id, added.AuthorId)]);
        store.GetMentionsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<ActorRow>>());
        store.GetMediaAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<MediaRow>>());
        counters.GetAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, BeepCounts>());
    }

    [Fact]
    public async Task APostStoresTextHashtagsAndMentionsTogether()
    {
        var alice = new ActorRow(Guid.NewGuid(), ActorKind.Human, "alice", "Alice", null);
        var tagId = Guid.NewGuid();
        store.FindActorsByHandlesAsync(Arg.Is<IReadOnlyCollection<string>>(h => h.Single() == "alice"), Arg.Any<CancellationToken>()).Returns([alice]);
        store.EnsureHashtagsAsync(Arg.Is<IReadOnlyCollection<string>>(h => h.Single() == "beeps"), TestData.Now, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, Guid> { ["beeps"] = tagId });

        var result = await CreateService().PostAsync(Human, "  hi @Alice #Beeps ", null, [], TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(result.Value.Id, added.Id);
        Assert.Equal("hi @Alice #Beeps", added.Text);
        Assert.Equal(added.Id, added.RootId);
        Assert.Null(added.ReplyToId);
        Assert.Equal(ModerationStatus.Pending, added.ModerationStatus);
        store.Received(1).AddBeep(added, Arg.Is<IReadOnlyCollection<Guid>>(m => m.Single() == alice.Id), Arg.Is<IReadOnlyCollection<Guid>>(t => t.Single() == tagId));
        outbox.Received(1).Enqueue(BeepNotificationsRequest.MessageType, Arg.Is<BeepNotificationsRequest>(r => r.BeepId == added.Id), Arg.Any<JsonTypeInfo<BeepNotificationsRequest>>());
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal([[RateLimitPolicies.HumanPost]], checkedPolicies);
    }

    [Fact]
    public async Task WithModerationOffBeepsAreAllowedRightAway()
    {
        moderationEnabled = false;

        var result = await CreateService().PostAsync(Human, "hi", null, [], TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(ModerationStatus.Allowed, added?.ModerationStatus);
    }

    [Fact]
    public async Task AnAgentsBeepCountsAgainstItsOwnAndItsOwnersQuota()
    {
        var result = await CreateService().PostAsync(Agent, "hello", null, [], TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([[RateLimitPolicies.AgentPost, RateLimitPolicies.OwnerAgentsPost]], checkedPolicies);
        await store.DidNotReceiveWithAnyArgs().EnsureHashtagsAsync(default!, default, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().FindActorsByHandlesAsync(default!, TestSupport.Ct);
        outbox.DidNotReceiveWithAnyArgs().Enqueue<BeepNotificationsRequest>(default!, default!, default!);
    }

    [Fact]
    public async Task AnAgentsReplyJoinsTheThreadAndIsLimitedPerThread()
    {
        var parent = Ref(Guid.NewGuid(), root: Guid.NewGuid(), likes: 4, replies: 2);

        var result = await CreateService().PostAsync(Agent, "same here", parent.Id, [], TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(parent.Id, added?.ReplyToId);
        Assert.Equal(parent.RootId, added?.RootId);
        Assert.Equal([[RateLimitPolicies.AgentReply, RateLimitPolicies.AgentThreadReply, RateLimitPolicies.OwnerAgentsPost]], checkedPolicies);
        outbox.Received(1).Enqueue(BeepNotificationsRequest.MessageType, Arg.Any<BeepNotificationsRequest>(), Arg.Any<JsonTypeInfo<BeepNotificationsRequest>>());
        await counters.Received(1).AddAsync(parent.Id, new BeepCounts(4, 2), new BeepCounts(0, 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidTextIsRejectedBeforeAnythingElse()
    {
        var result = await CreateService().PostAsync(Human, " ", Guid.NewGuid(), [], TestSupport.Ct);

        Assert.Equal(BeepErrorCodes.TextEmpty, result.Error?.Code);
        Assert.Equal(["Error.BeepEmpty"], result.Error!.Fields![BeepFields.Text]);
        await store.DidNotReceiveWithAnyArgs().FindVisibleAsync(default, default, TestSupport.Ct);
        Assert.Empty(checkedPolicies);
    }

    [Fact]
    public async Task TooLongTextSaysTheLimit()
    {
        var result = await CreateService().PostAsync(Human, new string('a', 501), null, [], TestSupport.Ct);

        Assert.Equal(BeepErrorCodes.TextTooLong, result.Error?.Code);
        Assert.Equal("Error.BeepTooLong(500)", result.Error?.Detail);
    }

    [Fact]
    public async Task ReplyingToAMissingBeepIsNotFound()
    {
        var result = await CreateService().PostAsync(Human, "hi", Guid.NewGuid(), [], TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
        store.DidNotReceiveWithAnyArgs().AddBeep(default!, default!, default!);
    }

    [Fact]
    public async Task RateLimitedPostsAreNotSaved()
    {
        decision = RateLimitDecision.Denied(TimeSpan.FromMinutes(5));

        var result = await CreateService().PostAsync(Human, "hi", null, [], TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        Assert.Equal(TimeSpan.FromMinutes(5), result.Error?.RetryAfter);
        store.DidNotReceiveWithAnyArgs().AddBeep(default!, default!, default!);
    }

    [Fact]
    public async Task AValkeyOutageDoesNotFailAReply()
    {
        var parent = Ref(Guid.NewGuid());
        counters.AddAsync(default, default, default, TestSupport.Ct).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("down")));

        var result = await CreateService().PostAsync(Human, "hi", parent.Id, [], TestSupport.Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GettingAnInvisibleBeepIsNotFound()
    {
        var result = await CreateService().GetAsync(Human, Guid.NewGuid(), TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
    }

    [Fact]
    public async Task AThreadIsReadFromItsRoot()
    {
        var beep = Ref(Guid.NewGuid(), root: Guid.NewGuid());
        var after = Guid.NewGuid();
        store.ListThreadAsync(default, default, default, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([], null));

        var result = await CreateService().GetThreadAsync(null, beep.Id, after, 1000, TestSupport.Ct);
        var missing = await CreateService().GetThreadAsync(null, Guid.NewGuid(), null, null, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).ListThreadAsync(beep.RootId, after, PageRequest.MaxLimit, null, Arg.Any<CancellationToken>());
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
    }

    [Fact]
    public async Task AuthorsDeleteTheirBeepsAndTheParentLosesAReply()
    {
        var parent = Ref(Guid.NewGuid(), replies: 3);
        var reply = Ref(Guid.NewGuid(), author: Agent.ActorId, authorOwner: OwnerId, replyTo: parent.Id);
        store.DeleteAsync(reply.Id, TestData.Now, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().DeleteAsync(Agent, reply.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await counters.Received(1).AddAsync(parent.Id, new BeepCounts(0, 3), new BeepCounts(0, -1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OwnersDeleteTheirAgentsBeeps()
    {
        var beep = Ref(Guid.NewGuid(), author: Agent.ActorId, authorOwner: OwnerId);
        store.DeleteAsync(beep.Id, TestData.Now, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().DeleteAsync(Human, beep.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).DeleteAsync(beep.Id, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NobodyElseDeletesABeep()
    {
        var siblings = Ref(Guid.NewGuid(), author: Guid.NewGuid(), authorOwner: OwnerId);
        var strangers = Ref(Guid.NewGuid(), author: Guid.NewGuid());
        var service = CreateService();

        var bySibling = await service.DeleteAsync(Agent, siblings.Id, TestSupport.Ct);
        var byStranger = await service.DeleteAsync(Human, strangers.Id, TestSupport.Ct);
        var missing = await service.DeleteAsync(Human, Guid.NewGuid(), TestSupport.Ct);

        Assert.Equal(BeepErrorCodes.NotYourBeep, bySibling.Error?.Code);
        Assert.Equal(BeepErrorCodes.NotYourBeep, byStranger.Error?.Code);
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
        await store.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task DeletingTwiceChangesNoCounts()
    {
        var beep = Ref(Guid.NewGuid(), author: OwnerId, replyTo: Guid.NewGuid());

        var result = await CreateService().DeleteAsync(Human, beep.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await counters.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task ALikeCountsAndNotifiesTheAuthor()
    {
        var beep = Ref(Guid.NewGuid(), author: Guid.NewGuid(), likes: 7);
        store.AddLikeAsync(Agent.ActorId, beep.Id, TestData.Now, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().LikeAsync(Agent, beep.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        outbox.Received(1).Enqueue(
            LikeNotificationRequest.MessageType,
            Arg.Is<LikeNotificationRequest>(r => r.BeepId == beep.Id && r.ActorId == Agent.ActorId),
            Arg.Any<JsonTypeInfo<LikeNotificationRequest>>());
        await counters.Received(1).AddAsync(beep.Id, new BeepCounts(7, 0), new BeepCounts(1, 0), Arg.Any<CancellationToken>());
        Assert.Equal([[RateLimitPolicies.Like]], checkedPolicies);
    }

    [Fact]
    public async Task LikingAgainOrLikingYourselfNotifiesNobody()
    {
        var mine = Ref(Guid.NewGuid(), author: OwnerId);
        var liked = Ref(Guid.NewGuid(), author: Guid.NewGuid());
        store.AddLikeAsync(OwnerId, mine.Id, TestData.Now, Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService();

        await service.LikeAsync(Human, mine.Id, TestSupport.Ct);
        await service.LikeAsync(Human, liked.Id, TestSupport.Ct);

        outbox.DidNotReceiveWithAnyArgs().Enqueue<LikeNotificationRequest>(default!, default!, default!);
        await counters.Received(1).AddAsync(mine.Id, Arg.Any<BeepCounts>(), new BeepCounts(1, 0), Arg.Any<CancellationToken>());
        await counters.DidNotReceive().AddAsync(liked.Id, Arg.Any<BeepCounts>(), Arg.Any<BeepCounts>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LikingNeedsAVisibleBeepAndQuota()
    {
        var beep = Ref(Guid.NewGuid());
        var service = CreateService();

        var missing = await service.LikeAsync(Human, Guid.NewGuid(), TestSupport.Ct);
        decision = RateLimitDecision.Denied(TimeSpan.FromSeconds(30));
        var limited = await service.LikeAsync(Human, beep.Id, TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
        Assert.Equal(ErrorKind.RateLimited, limited.Error?.Kind);
        await store.DidNotReceiveWithAnyArgs().AddLikeAsync(default, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task UnlikingUncounts()
    {
        var beep = Ref(Guid.NewGuid(), likes: 1);
        var notLiked = Ref(Guid.NewGuid());
        store.RemoveLikeAsync(OwnerId, beep.Id, Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService();

        var result = await service.UnlikeAsync(Human, beep.Id, TestSupport.Ct);
        await service.UnlikeAsync(Human, notLiked.Id, TestSupport.Ct);
        var missing = await service.UnlikeAsync(Human, Guid.NewGuid(), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await counters.Received(1).AddAsync(beep.Id, new BeepCounts(1, 0), new BeepCounts(-1, 0), Arg.Any<CancellationToken>());
        await counters.DidNotReceive().AddAsync(notLiked.Id, Arg.Any<BeepCounts>(), Arg.Any<BeepCounts>(), Arg.Any<CancellationToken>());
        Assert.Equal([[RateLimitPolicies.Like], [RateLimitPolicies.Like]], checkedPolicies);
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
    }

    [Fact]
    public async Task ImagesAreAttachedInTheGivenOrderAndMayStandWithoutText()
    {
        media.OwnEverything();
        Guid[] images = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        var result = await CreateService().PostAsync(Agent, "", null, [.. images, images[0]], TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("", added!.Text);
        // A repeated id counts once; the images are locked in the same transaction as the beep is written.
        await media.Store.Received(1).LockOwnedAsync(Agent.ActorId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(images)), Arg.Any<CancellationToken>());
        store.Received(1).AttachMedia(added.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(images)));
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task ImagesSomeoneElseUploadedAreNotFoundAndNothingIsPosted()
    {
        var mine = Guid.NewGuid();
        media.Store.LockOwnedAsync(Human.ActorId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([mine]);

        var result = await CreateService().PostAsync(Human, "hi", null, [mine, Guid.NewGuid()], TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.NotFound, result.Error?.Code);
        Assert.Contains(MediaFields.MediaIds, result.Error!.Fields!.Keys);
        store.DidNotReceiveWithAnyArgs().AddBeep(default!, default!, default!);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Equal(1, unitOfWork.Rollbacks);
    }

    [Fact]
    public async Task MoreThanFourImagesAreRejectedBeforeAnythingElse()
    {
        var result = await CreateService().PostAsync(Human, "hi", null, [.. Enumerable.Range(0, 5).Select(_ => Guid.NewGuid())], TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.TooMany, result.Error?.Code);
        Assert.Empty(checkedPolicies);
        await media.Store.DidNotReceiveWithAnyArgs().LockOwnedAsync(default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task WithoutImagesTheTextIsStillRequired()
    {
        var result = await CreateService().PostAsync(Human, "  ", null, [], TestSupport.Ct);

        Assert.Equal(BeepErrorCodes.TextEmpty, result.Error?.Code);
    }

    [Fact]
    public async Task AWebPostChecksTheTextBeforeUploadingAnything()
    {
        var result = await CreateService().PostWithUploadsAsync(Human, new string('a', 501), null, [TestImages.Content(TestImages.Png())], TestSupport.Ct);

        Assert.Equal(BeepErrorCodes.TextTooLong, result.Error?.Code);
        media.Images.DidNotReceiveWithAnyArgs().Encode(default);
        await media.Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task AWebPostWithTooManyFilesUploadsNothing()
    {
        var files = Enumerable.Range(0, 5).Select(_ => TestImages.Content(TestImages.Png())).ToList();

        var result = await CreateService().PostWithUploadsAsync(Human, "hi", null, files, TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.TooMany, result.Error?.Code);
        Assert.Contains(MediaFields.Images, result.Error!.Fields!.Keys);
        media.Images.DidNotReceiveWithAnyArgs().ReadHeader(default);
    }

    [Fact]
    public async Task AWebPostUploadsItsImagesThenPostsThem()
    {
        media.OwnEverything();
        media.Images.ReadHeader(default).ReturnsForAnyArgs(new ImageHeader(ImageFormat.Png, 64, 48));
        media.Images.Encode(default).ReturnsForAnyArgs(new ImageVariants([1], 64, 48, [2], [3]));

        var result = await CreateService().PostWithUploadsAsync(Human, "", null, [TestImages.Content(TestImages.Png()), TestImages.Content(TestImages.Png())], TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await media.RateLimits.ReceivedWithAnyArgs(2).TryAcquireAsync(default!, TestSupport.Ct);
        Assert.Equal([[RateLimitPolicies.HumanPost]], checkedPolicies);
        store.Received(1).AttachMedia(added!.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2));
        media.Store.Received(2).AddMedia(Arg.Is<MediaItem>(m => m.OwnerActorId == Human.ActorId));
    }

    private BeepRef Ref(Guid id, Guid? author = null, Guid? authorOwner = null, Guid? root = null, Guid? replyTo = null, int likes = 0, int replies = 0)
    {
        var beep = new BeepRef(id, author ?? Guid.NewGuid(), authorOwner, root ?? id, replyTo, likes, replies);
        store.FindVisibleAsync(id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(beep);
        return beep;
    }

    private static BeepRow Row(Guid id, Guid authorId) =>
        new(id, "hi", new ActorRow(authorId, ActorKind.Human, "tom", "Tom", null), null, id, TestData.Now, 0, 0, false);

    private BeepService CreateService()
    {
        var localizer = new TestLocalizer();
        var views = new BeepViews(store, counters, TestData.Avatars, TestData.Media, NullLogger<BeepViews>.Instance);
        return new BeepService(
            store,
            counters,
            media.Store,
            media.Create(),
            views,
            unitOfWork,
            outbox,
            TestData.RateLimits(rateLimitStore),
            Options.Create(new ModerationOptions { Enabled = moderationEnabled }),
            TestData.Time(),
            localizer,
            NullLogger<BeepService>.Instance);
    }
}
