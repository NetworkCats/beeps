using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Moderation;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Moderation;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Beeps.Tests.Infrastructure.Moderation;

public sealed class ModerationProcessorTests
{
    private static readonly Guid BeepId = Guid.NewGuid();

    private readonly IModerationJobStore jobs = Substitute.For<IModerationJobStore>();
    private readonly IModerationStore store = Substitute.For<IModerationStore>();
    private readonly IContentModerator moderator = Substitute.For<IContentModerator>();
    private readonly ModerationOptions options = new()
    {
        MaxAttempts = 5,
        RetryBaseDelay = TimeSpan.FromSeconds(15),
        RetryMaxDelay = TimeSpan.FromMinutes(10),
    };

    public ModerationProcessorTests()
    {
        store.GetSubjectAsync(BeepId, Arg.Any<CancellationToken>())
            .Returns(new ModerationSubject(BeepId, "hello", ModerationStatus.Pending, false, Guid.NewGuid(), null, null, null));
        store.SetStatusAsync(default, default!, default, TestSupport.Ct).ReturnsForAnyArgs(true);
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(4, 120)]
    [InlineData(10, 600)]
    public void RetriesBackOffExponentiallyUpToTheCap(int attempts, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), ModerationProcessor.RetryDelay(attempts, options));

    [Fact]
    public async Task AReviewedBeepCompletesItsJob()
    {
        moderator.ModerateAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(
            new ModerationVerdict(ModerationDecision.Allow, [], ModerationSeverity.None, "Fine.", "m", null, 1, null));
        var job = new ClaimedModerationJob(Guid.NewGuid(), BeepId, 1);

        await Create().ProcessAsync(job, TestSupport.Ct);

        await jobs.Received(1).CompleteAsync(job.Id, Arg.Any<CancellationToken>());
        await jobs.DidNotReceiveWithAnyArgs().RescheduleAsync(default, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task AFailedAttemptIsRetriedLater()
    {
        moderator.ModerateAsync(default!, TestSupport.Ct).ThrowsAsyncForAnyArgs(new ContentModerationException("OpenRouter answered 429"));
        var job = new ClaimedModerationJob(Guid.NewGuid(), BeepId, 2);

        await Create().ProcessAsync(job, TestSupport.Ct);

        await jobs.Received(1).RescheduleAsync(job.Id, TestData.Now.AddSeconds(30), "ContentModerationException: OpenRouter answered 429", Arg.Any<CancellationToken>());
        await jobs.DidNotReceiveWithAnyArgs().CompleteAsync(default, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().SetStatusAsync(default, default!, default, TestSupport.Ct);
    }

    [Fact]
    public async Task TheLastFailedAttemptLeavesTheBeepToTheAdmins()
    {
        moderator.ModerateAsync(default!, TestSupport.Ct).ThrowsAsyncForAnyArgs(new ContentModerationException("bad answer"));
        var job = new ClaimedModerationJob(Guid.NewGuid(), BeepId, 5);

        await Create().ProcessAsync(job, TestSupport.Ct);

        await store.Received(1).SetStatusAsync(BeepId, Arg.Any<IReadOnlyCollection<ModerationStatus>>(), ModerationStatus.Failed, Arg.Any<CancellationToken>());
        await jobs.Received(1).CompleteAsync(job.Id, Arg.Any<CancellationToken>());
        await jobs.DidNotReceiveWithAnyArgs().RescheduleAsync(default, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task AJobThatOutlivedItsAttemptsIsNotTriedAgain()
    {
        var job = new ClaimedModerationJob(Guid.NewGuid(), BeepId, 6);

        await Create().ProcessAsync(job, TestSupport.Ct);

        await moderator.DidNotReceiveWithAnyArgs().ModerateAsync(default!, TestSupport.Ct);
        await store.Received(1).SetStatusAsync(BeepId, Arg.Any<IReadOnlyCollection<ModerationStatus>>(), ModerationStatus.Failed, Arg.Any<CancellationToken>());
        await jobs.Received(1).CompleteAsync(job.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DevelopmentModeratorAllowsAllButMarkedBeeps()
    {
        var development = new DevelopmentContentModerator();

        var allowed = await development.ModerateAsync(new ModerationInput("hello", null), TestSupport.Ct);
        var hidden = await development.ModerateAsync(new ModerationInput($"x {DevelopmentContentModerator.HideMarker}", null), TestSupport.Ct);

        Assert.Equal(ModerationDecision.Allow, allowed.Decision);
        Assert.Equal(ModerationDecision.Hide, hidden.Decision);
        await Assert.ThrowsAsync<ContentModerationException>(() => development.ModerateAsync(new ModerationInput(DevelopmentContentModerator.FailMarker, null), TestSupport.Ct));
    }

    private ModerationProcessor Create()
    {
        var beeps = Substitute.For<IBeepStore>();
        var counters = Substitute.For<IBeepCounterStore>();
        var liveEvents = Substitute.For<ILiveEventStore>();
        var moderation = new ModerationService(
            store,
            moderator,
            TestServices.Beeps(beeps, counters, liveEvents),
            new ImmediateUnitOfWork(),
            Substitute.For<IOutbox>(),
            TestData.Live(liveEvents, counters),
            Options.Create(options),
            TestData.Time(),
            NullLogger<ModerationService>.Instance);
        return new ModerationProcessor(jobs, moderation, Options.Create(options), TestData.Time(), NullLogger<ModerationProcessor>.Instance);
    }
}
