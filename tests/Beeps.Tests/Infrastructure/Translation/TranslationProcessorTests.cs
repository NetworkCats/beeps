using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Translation;
using Beeps.Infrastructure.Queues;
using Beeps.Infrastructure.Translation;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Beeps.Tests.Infrastructure.Translation;

public sealed class TranslationProcessorTests
{
    private static readonly Guid BeepId = Guid.NewGuid();

    private readonly ITranslationJobStore jobs = Substitute.For<ITranslationJobStore>();
    private readonly ITranslationStore store = Substitute.For<ITranslationStore>();
    private readonly ITranslator translator = Substitute.For<ITranslator>();
    private readonly TranslationOptions options = new() { MaxAttempts = 3 };

    public TranslationProcessorTests() =>
        store.GetSubjectAsync(BeepId, Arg.Any<CancellationToken>()).Returns(new TranslationSubject(BeepId, "hello", ModerationStatus.Allowed, false, false));

    [Fact]
    public async Task ATranslatedBeepCompletesItsJob()
    {
        translator.TranslateAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new TranslationVerdict("en", "", "你好", "m", null, 1, null));
        var job = new ClaimedBeepJob(Guid.NewGuid(), BeepId, 1);

        await Create().ProcessAsync(job, TestSupport.Ct);

        store.Received(1).Add(Arg.Any<BeepTranslation>());
        await jobs.Received(1).CompleteAsync(job.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedAttemptIsRetriedLater()
    {
        translator.TranslateAsync(default!, TestSupport.Ct).ThrowsAsyncForAnyArgs(new TranslationException("OpenRouter answered 429"));
        var job = new ClaimedBeepJob(Guid.NewGuid(), BeepId, 1);

        await Create().ProcessAsync(job, TestSupport.Ct);

        await jobs.Received(1).RescheduleAsync(job.Id, TestData.Now.AddSeconds(15), "TranslationException: OpenRouter answered 429", Arg.Any<CancellationToken>());
        await jobs.DidNotReceiveWithAnyArgs().CompleteAsync(default, TestSupport.Ct);
    }

    [Fact]
    public async Task AfterTheLastAttemptTheBeepStaysAsWritten()
    {
        translator.TranslateAsync(default!, TestSupport.Ct).ThrowsAsyncForAnyArgs(new TranslationException("bad answer"));
        var job = new ClaimedBeepJob(Guid.NewGuid(), BeepId, 3);

        await Create().ProcessAsync(job, TestSupport.Ct);

        await jobs.Received(1).CompleteAsync(job.Id, Arg.Any<CancellationToken>());
        await jobs.DidNotReceiveWithAnyArgs().RescheduleAsync(default, default, default!, TestSupport.Ct);
        store.DidNotReceiveWithAnyArgs().Add(default!);
    }

    private TranslationProcessor Create()
    {
        var translation = new TranslationService(
            store,
            translator,
            new ImmediateUnitOfWork(),
            TestData.Live(Substitute.For<ILiveEventStore>()),
            TestData.Time(),
            NullLogger<TranslationService>.Instance);
        return new TranslationProcessor(jobs, translation, Options.Create(options), TestData.Time(), NullLogger<TranslationProcessor>.Instance);
    }
}
