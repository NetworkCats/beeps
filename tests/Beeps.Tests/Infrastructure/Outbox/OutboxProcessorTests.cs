using Beeps.Features.Shared;
using Beeps.Infrastructure;
using Beeps.Infrastructure.Outbox;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Beeps.Tests.Infrastructure.Outbox;

public sealed class OutboxProcessorTests
{
    private readonly IOutboxStore store = Substitute.For<IOutboxStore>();
    private readonly IOutboxHandler handler = Substitute.For<IOutboxHandler>();
    private readonly TimeProvider time = TestData.Time();
    private readonly OutboxOptions options = new()
    {
        BatchSize = 10,
        MaxAttempts = 3,
        RetryBaseDelay = TimeSpan.FromSeconds(10),
        RetryMaxDelay = TimeSpan.FromMinutes(1),
        LeaseDuration = TimeSpan.FromMinutes(5),
    };

    public OutboxProcessorTests()
    {
        handler.Type.Returns("test.message");
        handler.Queue.Returns(OutboxQueues.Main);
    }

    [Fact]
    public async Task EachQueueClaimsItsOwnTypesAndTheMainQueueEverythingElse()
    {
        var search = Substitute.For<IOutboxHandler>();
        search.Type.Returns("search.thing");
        search.Queue.Returns(OutboxQueues.Search);
        Claim();
        var processor = new OutboxProcessor(store, [handler, search], Options.Create(options), time, NullLogger<OutboxProcessor>.Instance);

        await processor.ProcessBatchAsync(OutboxQueues.Main, TestSupport.Ct);
        await processor.ProcessBatchAsync(OutboxQueues.Search, TestSupport.Ct);

        // A slow search engine then holds up only the search queue.
        await store.Received(1).ClaimAsync(Arg.Is<OutboxTypes>(t => t.Except && t.Types.Single() == "search.thing"), 10, TestData.Now, TestData.Now.AddMinutes(5), Arg.Any<CancellationToken>());
        await store.Received(1).ClaimAsync(Arg.Is<OutboxTypes>(t => !t.Except && t.Types.Single() == "search.thing"), 10, TestData.Now, TestData.Now.AddMinutes(5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClaimsDueMessagesWithALease()
    {
        Claim();

        var claimed = await CreateProcessor().ProcessBatchAsync(OutboxQueues.Main, TestSupport.Ct);

        Assert.Equal(0, claimed);
        await store.Received(1).ClaimAsync(Arg.Any<OutboxTypes>(), 10, TestData.Now, TestData.Now.AddMinutes(5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulMessagesAreCompleted()
    {
        var message = Message(attempts: 1);
        Claim(message);

        var claimed = await CreateProcessor().ProcessBatchAsync(OutboxQueues.Main, TestSupport.Ct);

        Assert.Equal(1, claimed);
        await handler.Received(1).HandleAsync(message, Arg.Any<CancellationToken>());
        await store.Received(1).CompleteAsync(message.Id, Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().RescheduleAsync(default, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task WhatIsLeftOfABatchOnceItsLeaseRanOutIsNotRunHereToo()
    {
        var clock = new FixedTimeProvider(TestData.Now);
        var slow = Message(attempts: 1);
        var left = Message(attempts: 1);
        Claim(slow, left);
        // Another worker may claim the rest again from here on.
        handler.HandleAsync(slow, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            clock.Now += options.LeaseDuration;
            return Task.CompletedTask;
        });

        await new OutboxProcessor(store, [handler], Options.Create(options), clock, NullLogger<OutboxProcessor>.Instance).ProcessBatchAsync(OutboxQueues.Main, TestSupport.Ct);

        await store.Received(1).CompleteAsync(slow.Id, Arg.Any<CancellationToken>());
        await handler.DidNotReceive().HandleAsync(left, Arg.Any<CancellationToken>());
        await store.DidNotReceive().CompleteAsync(left.Id, Arg.Any<CancellationToken>());
        // Never started here, so none of its attempts is used up.
        await store.DidNotReceive().StartAttemptAsync(left.Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnlyTheMessageThatStartedCountsAnAttempt()
    {
        var crashing = Message(attempts: 1);
        var waiting = Message(attempts: 1);
        Claim(crashing, waiting);
        // What a crash of the whole process looks like from here: nothing after it runs.
        handler.HandleAsync(Arg.Is<OutboxEnvelope>(m => m.Id == crashing.Id), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProcessor().ProcessBatchAsync(OutboxQueues.Main, stopped.Token));

        await store.Received(1).StartAttemptAsync(crashing.Id, TestData.Now.AddMinutes(5), TestData.Now.AddMinutes(5), Arg.Any<CancellationToken>());
        await store.DidNotReceive().StartAttemptAsync(waiting.Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMessageGoneBeforeItStartsIsSkipped()
    {
        var message = Message(attempts: 1);
        Claim(message);
        store.StartAttemptAsync(message.Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns((int?)null);

        await CreateProcessor().ProcessBatchAsync(OutboxQueues.Main, TestSupport.Ct);

        await handler.DidNotReceiveWithAnyArgs().HandleAsync(default!, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().CompleteAsync(default, TestSupport.Ct);
    }

    [Fact]
    public async Task ALongRunningMessageKeepsItsLease()
    {
        var message = Message(attempts: 1);
        Claim(message);
        var extended = new TaskCompletionSource();
        store.ExtendLeaseAsync(message.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            extended.TrySetResult();
            return Task.CompletedTask;
        });
        // Still running when half its lease is gone: it must not be claimed again and run beside itself.
        handler.HandleAsync(message, Arg.Any<CancellationToken>()).Returns(_ => extended.Task.WaitAsync(TimeSpan.FromSeconds(10), TestSupport.Ct));
        var shortLease = new OutboxOptions { BatchSize = 10, MaxAttempts = 3, LeaseDuration = TimeSpan.FromMilliseconds(100) };

        await new OutboxProcessor(store, [handler], Options.Create(shortLease), TimeProvider.System, NullLogger<OutboxProcessor>.Instance)
            .ProcessBatchAsync(OutboxQueues.Main, TestSupport.Ct);

        await store.Received().ExtendLeaseAsync(message.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await store.Received(1).CompleteAsync(message.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedMessagesAreRescheduledWithBackoff()
    {
        var message = Message(attempts: 2);
        Claim(message);
        handler.HandleAsync(message, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("smtp down"));

        await CreateProcessor().ProcessBatchAsync(OutboxQueues.Main, TestSupport.Ct);

        await store.Received(1).RescheduleAsync(
            message.Id,
            TestData.Now.AddSeconds(20),
            Arg.Is<string>(e => e.Contains("smtp down", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().CompleteAsync(default, TestSupport.Ct);
    }

    [Fact]
    public async Task LastAttemptFailureGoesToTheDeadLetterTable()
    {
        var message = Message(attempts: 3);
        Claim(message);
        handler.HandleAsync(message, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        await CreateProcessor().ProcessBatchAsync(OutboxQueues.Main, TestSupport.Ct);

        await store.Received(1).DeadLetterAsync(message.Id, Arg.Is<string>(e => e.Contains("boom", StringComparison.Ordinal)), TestData.Now, Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().RescheduleAsync(default, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task UnknownTypesAreRetriedNotDropped()
    {
        var message = Message(attempts: 1, type: "from.a.newer.release");
        Claim(message);

        await CreateProcessor().ProcessBatchAsync(OutboxQueues.Main, TestSupport.Ct);

        await store.Received(1).RescheduleAsync(message.Id, TestData.Now.AddSeconds(10), Arg.Is<string>(e => e.Contains("from.a.newer.release", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MessagesThatOutlivedTheirAttemptsAreNotRunAgain()
    {
        var message = Message(attempts: 4);
        Claim(message);

        await CreateProcessor().ProcessBatchAsync(OutboxQueues.Main, TestSupport.Ct);

        await handler.DidNotReceiveWithAnyArgs().HandleAsync(default!, TestSupport.Ct);
        await store.Received(1).DeadLetterAsync(message.Id, Arg.Any<string>(), TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShutdownLeavesTheMessageClaimed()
    {
        using var shutdown = new CancellationTokenSource();
        var message = Message(attempts: 1);
        Claim(message);
        handler.HandleAsync(message, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await shutdown.CancelAsync();
            shutdown.Token.ThrowIfCancellationRequested();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProcessor().ProcessBatchAsync(OutboxQueues.Main, shutdown.Token));

        await store.DidNotReceiveWithAnyArgs().RescheduleAsync(default, default, default!, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().DeadLetterAsync(default, default!, default, TestSupport.Ct);
    }

    [Fact]
    public void DuplicateHandlersAreRejected()
    {
        var other = Substitute.For<IOutboxHandler>();
        other.Type.Returns("test.message");

        Assert.Throws<InvalidOperationException>(() => new OutboxProcessor(store, [handler, other], Options.Create(options), time, NullLogger<OutboxProcessor>.Instance));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 60)]
    [InlineData(1000, 60)]
    public void RetryDelayDoublesUpToTheCap(int attempts, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), FailedAttempts.RetryDelay(attempts, options.RetryBaseDelay, options.RetryMaxDelay));

    private static OutboxEnvelope Message(int attempts, string type = "test.message") =>
        new(Guid.CreateVersion7(TestData.Now), type, """{"id":1}""", attempts, TestData.Now.AddMinutes(-1));

    /// <summary>Claims <paramref name="messages"/>, each counted at its <see cref="OutboxEnvelope.Attempts"/> once started.</summary>
    private void Claim(params OutboxEnvelope[] messages)
    {
        store.ClaimAsync(default!, default, default, default, TestSupport.Ct)
            .ReturnsForAnyArgs([.. messages.Select(m => m with { Attempts = m.Attempts - 1 })]);
        foreach (var message in messages)
        {
            store.StartAttemptAsync(message.Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(message.Attempts);
        }
    }

    private OutboxProcessor CreateProcessor() =>
        new(store, [handler], Options.Create(options), time, NullLogger<OutboxProcessor>.Instance);
}
