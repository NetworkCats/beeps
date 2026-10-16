using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Moderation;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Beeps.Tests.Features.Moderation;

public sealed class ModerationServiceTests
{
    private static readonly Guid BeepId = Guid.NewGuid();
    private static readonly Guid AuthorId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();

    private readonly IModerationStore store = Substitute.For<IModerationStore>();
    private readonly IContentModerator moderator = Substitute.For<IContentModerator>();
    private readonly IBeepStore beeps = Substitute.For<IBeepStore>();
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();
    private readonly IOutbox outbox = Substitute.For<IOutbox>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly List<ModerationResult> results = [];

    public ModerationServiceTests()
    {
        store.When(s => s.AddResult(Arg.Any<ModerationResult>())).Do(ci => results.Add(ci.Arg<ModerationResult>()));
        store.SetStatusAsync(default, default!, default, TestSupport.Ct).ReturnsForAnyArgs(true);
        beeps.FindVisibleAsync(ParentId, null, Arg.Any<CancellationToken>()).Returns(new BeepRef(ParentId, Guid.NewGuid(), null, ParentId, null, 1, 3));
    }

    [Fact]
    public async Task AnAllowedBeepIsRecordedAndLeftAsItIs()
    {
        Subject(ModerationStatus.Pending);
        moderator.ModerateAsync(new ModerationInput("hello", null), Arg.Any<CancellationToken>()).Returns(Verdict(ModerationDecision.Allow));

        await CreateService().ReviewAsync(BeepId, TestSupport.Ct);

        var result = Assert.Single(results);
        Assert.Equal((BeepId, ModerationDecision.Allow, "test-model", "TestProvider", 420, 99), (result.BeepId, result.Decision, result.Model, result.Provider, result.LatencyMs, result.Tokens));
        await store.Received(1).SetStatusAsync(BeepId, Arg.Is<IReadOnlyCollection<ModerationStatus>>(s => s.SequenceEqual(new[] { ModerationStatus.Pending })), ModerationStatus.Allowed, Arg.Any<CancellationToken>());
        Assert.Equal(1, unitOfWork.Saves);
        outbox.DidNotReceiveWithAnyArgs().Enqueue<SearchBeepSyncRequest>(default!, default!, default!);
        Assert.Empty(TestData.LiveEvents(liveEvents, LiveEventTypes.BeepHidden));
    }

    [Fact]
    public async Task AHiddenReplyLeavesSearchPagesAndItsParentsCount()
    {
        Subject(ModerationStatus.Pending, replyTo: ParentId, replyToText: "what do you think?");
        moderator.ModerateAsync(new ModerationInput("hello", "what do you think?"), Arg.Any<CancellationToken>())
            .Returns(Verdict(ModerationDecision.Hide, ModerationCategories.Spam, ModerationCategories.AgentManipulation));

        await CreateService().ReviewAsync(BeepId, TestSupport.Ct);

        var result = Assert.Single(results);
        Assert.Equal([ModerationCategories.Spam, ModerationCategories.AgentManipulation], result.Categories);
        await store.Received(1).SetStatusAsync(BeepId, Arg.Any<IReadOnlyCollection<ModerationStatus>>(), ModerationStatus.Hidden, Arg.Any<CancellationToken>());
        outbox.Received(1).Enqueue(SearchBeepSyncRequest.MessageType, new SearchBeepSyncRequest(BeepId), Arg.Any<JsonTypeInfo<SearchBeepSyncRequest>>());
        Assert.Equal(
            [$$"""{"id":"{{BeepId:D}}","author_id":"{{AuthorId:D}}","author_owner_id":"{{OwnerId:D}}"}"""],
            TestData.LiveEvents(liveEvents, LiveEventTypes.BeepHidden));
        await counters.Received(1).AddAsync(ParentId, new BeepCounts(1, 3), new BeepCounts(0, -1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAdminsDecisionInTheMeantimeWins()
    {
        Subject(ModerationStatus.Pending, replyTo: ParentId);
        moderator.ModerateAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(Verdict(ModerationDecision.Hide, ModerationCategories.Other));
        store.SetStatusAsync(default, default!, default, TestSupport.Ct).ReturnsForAnyArgs(false);

        await CreateService().ReviewAsync(BeepId, TestSupport.Ct);

        Assert.Single(results);
        outbox.DidNotReceiveWithAnyArgs().Enqueue<SearchBeepSyncRequest>(default!, default!, default!);
        Assert.Empty(TestData.LiveEvents(liveEvents, LiveEventTypes.BeepHidden));
        await counters.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, TestSupport.Ct);
    }

    [Theory]
    [InlineData(ModerationStatus.Allowed, false)]
    [InlineData(ModerationStatus.HumanAllowed, false)]
    [InlineData(ModerationStatus.Pending, true)]
    public async Task OnlyLivePendingBeepsAreReviewed(ModerationStatus status, bool deleted)
    {
        Subject(status, deleted);

        await CreateService().ReviewAsync(BeepId, TestSupport.Ct);
        await CreateService().ReviewAsync(Guid.NewGuid(), TestSupport.Ct);

        await moderator.DidNotReceiveWithAnyArgs().ModerateAsync(default!, TestSupport.Ct);
        Assert.Empty(results);
    }

    [Fact]
    public async Task AFailedReviewThrowsAndChangesNothing()
    {
        Subject(ModerationStatus.Pending);
        moderator.ModerateAsync(default!, TestSupport.Ct).ThrowsAsyncForAnyArgs(new ContentModerationException("429"));

        await Assert.ThrowsAsync<ContentModerationException>(() => CreateService().ReviewAsync(BeepId, TestSupport.Ct));

        Assert.Empty(results);
        Assert.Equal(0, unitOfWork.Saves);
        await store.DidNotReceiveWithAnyArgs().SetStatusAsync(default, default!, default, TestSupport.Ct);
    }

    [Fact]
    public async Task GivingUpMarksAPendingBeepFailed()
    {
        await CreateService().GiveUpAsync(BeepId, "HttpRequestException: down", TestSupport.Ct);

        await store.Received(1).SetStatusAsync(
            BeepId, Arg.Is<IReadOnlyCollection<ModerationStatus>>(s => s.SequenceEqual(new[] { ModerationStatus.Pending })), ModerationStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OverlongAnswersAreCutToTheirColumns()
    {
        Subject(ModerationStatus.Pending);
        moderator.ModerateAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(
            Verdict(ModerationDecision.Allow) with { Reason = new string('r', 5000), Model = new string('m', 500) });

        await CreateService().ReviewAsync(BeepId, TestSupport.Ct);

        var result = Assert.Single(results);
        Assert.Equal(ModerationResult.ReasonMaxLength, result.Reason.Length);
        Assert.Equal(ModerationResult.ModelMaxLength, result.Model.Length);
    }

    private void Subject(ModerationStatus status, bool deleted = false, Guid? replyTo = null, string? replyToText = null) =>
        store.GetSubjectAsync(BeepId, Arg.Any<CancellationToken>())
            .Returns(new ModerationSubject(BeepId, "hello", status, deleted, AuthorId, OwnerId, replyTo, replyToText));

    private static ModerationVerdict Verdict(ModerationDecision decision, params string[] categories) =>
        new(decision, categories, decision == ModerationDecision.Hide ? ModerationSeverity.Medium : ModerationSeverity.None, "Because.", "test-model", "TestProvider", 420, 99);

    private ModerationService CreateService() => new(
        store,
        moderator,
        TestServices.Beeps(beeps, counters, liveEvents),
        unitOfWork,
        outbox,
        TestData.Live(liveEvents, counters),
        Options.Create(new ModerationOptions()),
        TestData.Time(),
        NullLogger<ModerationService>.Instance);
}
