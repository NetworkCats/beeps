using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Admin;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Features.Translation;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Admin;

public sealed class ReviewServiceTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid BeepId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();

    private readonly IModerationStore store = Substitute.For<IModerationStore>();
    private readonly IAdminStore admins = Substitute.For<IAdminStore>();
    private readonly IBeepStore beeps = Substitute.For<IBeepStore>();
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();
    private readonly IOutbox outbox = Substitute.For<IOutbox>();
    private readonly ITranslationJobs translationJobs = Substitute.For<ITranslationJobs>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly List<AdminAuditLog> audit = [];

    public ReviewServiceTests()
    {
        admins.IsActiveAdminAsync(AdminId, Arg.Any<CancellationToken>()).Returns(true);
        admins.When(s => s.AddAuditLog(Arg.Any<AdminAuditLog>())).Do(ci => audit.Add(ci.Arg<AdminAuditLog>()));
        beeps.FindVisibleAsync(ParentId, null, Arg.Any<CancellationToken>()).Returns(new BeepRef(ParentId, Guid.NewGuid(), null, ParentId, null, 0, 4));
    }

    [Fact]
    public async Task EveryMethodChecksTheCallerIsAnAdmin()
    {
        var service = Create();
        var outsider = Guid.NewGuid();

        Error?[] errors =
        [
            (await service.ListQueueAsync(outsider, null, TestSupport.Ct)).Error,
            (await service.GetBeepAsync(outsider, BeepId, TestSupport.Ct)).Error,
            (await service.KeepAsync(outsider, BeepId, [], TestSupport.Ct)).Error,
            (await service.DeleteAsync(outsider, BeepId, TestSupport.Ct)).Error,
            (await service.RemoveMediaAsync(outsider, BeepId, TestSupport.Ct)).Error,
        ];

        Assert.All(errors, e => Assert.Equal(AdminErrorCodes.NotAdmin, e?.Code));
        Assert.Empty(audit);
        await store.DidNotReceiveWithAnyArgs().GetSubjectAsync(default, TestSupport.Ct);
    }

    [Fact]
    public async Task TheQueueAndABeepAreReadForAdmins()
    {
        var after = Guid.NewGuid();
        var page = new CursorPage<ReviewQueueRow>([], null);
        store.ListQueueAsync(after, ReviewService.PageSize, Arg.Any<CancellationToken>()).Returns(page);
        var service = Create();

        var queue = await service.ListQueueAsync(AdminId, after, TestSupport.Ct);
        var missing = await service.GetBeepAsync(AdminId, BeepId, TestSupport.Ct);

        Assert.Same(page, queue.Value);
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
    }

    [Fact]
    public async Task RestoringAHiddenReplyBringsItBackEverywhere()
    {
        Subject(ModerationStatus.Hidden, replyTo: ParentId);
        store.SetStatusAsync(BeepId, Arg.Any<IReadOnlyCollection<ModerationStatus>>(), ModerationStatus.HumanAllowed, Arg.Any<CancellationToken>()).Returns(true);
        Guid[] seen = [Guid.NewGuid(), Guid.NewGuid()];
        store.ResolveReportsAsync(BeepId, seen, AdminId, TestData.Now, Arg.Any<CancellationToken>()).Returns(2);

        var result = await Create().KeepAsync(AdminId, BeepId, seen, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).DeleteJobAsync(BeepId, Arg.Any<CancellationToken>());
        outbox.Received(1).Enqueue(SearchBeepSyncRequest.MessageType, new SearchBeepSyncRequest(BeepId), Arg.Any<JsonTypeInfo<SearchBeepSyncRequest>>());
        var entry = Assert.Single(audit);
        Assert.Equal((AuditActions.KeepBeep, AuditTargets.Beep, BeepId, "Hidden -> HumanAllowed, reports resolved: 2"), (entry.Action, entry.TargetType, entry.TargetId, entry.Detail));
        Assert.Equal(1, unitOfWork.Saves);
        await counters.Received(1).AddAsync(ParentId, new BeepCounts(0, 4), new BeepCounts(0, 1), Arg.Any<CancellationToken>());
        // Open pages put it back where it belongs, and its author's loses the "under review" mark.
        Assert.Equal(["{}"], TestData.LiveEvents(liveEvents, LiveEventTypes.Resync));
        Assert.Equal([$$"""{"ids":["{{BeepId:D}}"]}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.BeepsChanged));
        // Let through only now: translated next.
        translationJobs.Received(1).Enqueue(BeepId);
    }

    [Fact]
    public async Task KeepingABeepTheModelAllowedQueuesNoSecondTranslation()
    {
        Subject(ModerationStatus.Allowed);
        Guid[] seen = [Guid.NewGuid()];
        store.SetStatusAsync(BeepId, Arg.Any<IReadOnlyCollection<ModerationStatus>>(), ModerationStatus.HumanAllowed, Arg.Any<CancellationToken>()).Returns(true);
        store.ResolveReportsAsync(BeepId, seen, AdminId, TestData.Now, Arg.Any<CancellationToken>()).Returns(1);

        var result = await Create().KeepAsync(AdminId, BeepId, seen, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("Allowed -> HumanAllowed, reports resolved: 1", Assert.Single(audit).Detail);
        // The model's review queued it already.
        translationJobs.DidNotReceiveWithAnyArgs().Enqueue(default);
    }

    [Fact]
    public async Task KeepingABeepTheModelJustHidRestoresIt()
    {
        Subject(ModerationStatus.Pending, replyTo: ParentId);
        store.LockStatusAsync(BeepId, Arg.Any<CancellationToken>()).Returns(ModerationStatus.Hidden);
        store.SetStatusAsync(BeepId, Arg.Any<IReadOnlyCollection<ModerationStatus>>(), ModerationStatus.HumanAllowed, Arg.Any<CancellationToken>()).Returns(true);

        await Create().KeepAsync(AdminId, BeepId, [], TestSupport.Ct);

        Assert.Equal("Hidden -> HumanAllowed, reports resolved: 0", Assert.Single(audit).Detail);
        await counters.Received(1).AddAsync(ParentId, Arg.Any<BeepCounts>(), new BeepCounts(0, 1), Arg.Any<CancellationToken>());
        Assert.Equal(["{}"], TestData.LiveEvents(liveEvents, LiveEventTypes.Resync));
    }

    [Fact]
    public async Task KeepingAReportedBeepClosesItsReports()
    {
        Subject(ModerationStatus.HumanAllowed, replyTo: ParentId);
        Guid[] seen = [Guid.NewGuid()];
        store.ResolveReportsAsync(BeepId, seen, AdminId, TestData.Now, Arg.Any<CancellationToken>()).Returns(1);

        var result = await Create().KeepAsync(AdminId, BeepId, seen, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("HumanAllowed, reports resolved: 1", Assert.Single(audit).Detail);
        outbox.DidNotReceiveWithAnyArgs().Enqueue<SearchBeepSyncRequest>(default!, default!, default!);
        await counters.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, TestSupport.Ct);
        // It never left any page.
        Assert.Empty(TestData.LiveEvents(liveEvents, LiveEventTypes.Resync));
    }

    [Fact]
    public async Task KeepingClosesOnlyTheReportsTheAdminSaw()
    {
        Subject(ModerationStatus.HumanAllowed);
        Guid[] seen = [Guid.NewGuid()];

        await Create().KeepAsync(AdminId, BeepId, seen, TestSupport.Ct);

        // One filed since the page was drawn stays open, and the beep in the queue.
        await store.Received(1).ResolveReportsAsync(BeepId, seen, AdminId, TestData.Now, Arg.Any<CancellationToken>());
        await store.DidNotReceive().ResolveReportsAsync(BeepId, AdminId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepingWhatIsAlreadyKeptChangesNothing()
    {
        Subject(ModerationStatus.HumanAllowed);

        var result = await Create().KeepAsync(AdminId, BeepId, [], TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(audit);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task DeletedOrMissingBeepsCannotBeKept()
    {
        Subject(ModerationStatus.Hidden, deleted: true);
        var service = Create();

        var deleted = await service.KeepAsync(AdminId, BeepId, [], TestSupport.Ct);
        var missing = await service.KeepAsync(AdminId, Guid.NewGuid(), [], TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, deleted.Error?.Kind);
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
    }

    [Fact]
    public async Task DeletingABeepRemovesItForEveryoneAndRecordsIt()
    {
        Subject(ModerationStatus.Failed, replyTo: ParentId);
        beeps.DeleteAsync(BeepId, TestData.Now, Arg.Any<CancellationToken>()).Returns(ModerationStatus.Failed);

        var result = await Create().DeleteAsync(AdminId, BeepId, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).ResolveReportsAsync(BeepId, AdminId, TestData.Now, Arg.Any<CancellationToken>());
        await store.Received(1).DeleteJobAsync(BeepId, Arg.Any<CancellationToken>());
        outbox.Received(1).Enqueue(SearchBeepSyncRequest.MessageType, new SearchBeepSyncRequest(BeepId), Arg.Any<JsonTypeInfo<SearchBeepSyncRequest>>());
        Assert.Equal(AuditActions.DeleteBeep, Assert.Single(audit).Action);
        Assert.Equal([$$"""{"id":"{{BeepId:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.BeepRemoved));
        await counters.Received(1).AddAsync(ParentId, Arg.Any<BeepCounts>(), new BeepCounts(0, -1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeletingAHiddenReplyLeavesItsParentsCountAlone()
    {
        // Read as pending, hidden by the model before the delete took its lock.
        Subject(ModerationStatus.Pending, replyTo: ParentId);
        beeps.DeleteAsync(BeepId, TestData.Now, Arg.Any<CancellationToken>()).Returns(ModerationStatus.Hidden);

        await Create().DeleteAsync(AdminId, BeepId, TestSupport.Ct);

        await counters.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task DeletingTwiceRecordsItOnce()
    {
        Subject(ModerationStatus.Allowed, deleted: true);

        var result = await Create().DeleteAsync(AdminId, BeepId, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(audit);
        Assert.Empty(TestData.LiveEvents(liveEvents, LiveEventTypes.BeepRemoved));
    }

    [Fact]
    public async Task RemovingImagesDeletesTheirObjects()
    {
        Subject(ModerationStatus.Allowed);
        store.RemoveMediaAsync(BeepId, Arg.Any<CancellationToken>()).Returns(["key1", "key2"]);

        var result = await Create().RemoveMediaAsync(AdminId, BeepId, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        outbox.Received(1).Enqueue(
            DeleteMediaObjectsRequest.MessageType,
            Arg.Is<DeleteMediaObjectsRequest>(r => string.Join(",", r.StorageKeys) == "key1,key2"),
            Arg.Any<JsonTypeInfo<DeleteMediaObjectsRequest>>());
        var entry = Assert.Single(audit);
        Assert.Equal((AuditActions.RemoveBeepMedia, "images: 2"), (entry.Action, entry.Detail));
        // Open pages draw the beep again, without them.
        Assert.Equal([$$"""{"ids":["{{BeepId:D}}"]}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.BeepsChanged));
    }

    [Fact]
    public async Task ABeepWithoutImagesHasNoneToRemove()
    {
        Subject(ModerationStatus.Allowed);
        store.RemoveMediaAsync(BeepId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await Create().RemoveMediaAsync(AdminId, BeepId, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(audit);
        outbox.DidNotReceiveWithAnyArgs().Enqueue<DeleteMediaObjectsRequest>(default!, default!, default!);
    }

    private void Subject(ModerationStatus status, bool deleted = false, Guid? replyTo = null) =>
        store.GetSubjectAsync(BeepId, Arg.Any<CancellationToken>())
            .Returns(new ModerationSubject(BeepId, "hello", status, deleted, Guid.NewGuid(), null, replyTo, null));

    private ReviewService Create()
    {
        var localizer = new TestLocalizer();
        return new ReviewService(
            store,
            beeps,
            TestServices.Beeps(beeps, counters, liveEvents),
            new AdminAccess(admins, TestData.Time(), localizer),
            unitOfWork,
            outbox,
            translationJobs,
            TestData.Live(liveEvents, counters),
            TestData.Inboxes(liveEvents),
            TestData.Time(),
            localizer,
            NullLogger<ReviewService>.Instance);
    }
}
