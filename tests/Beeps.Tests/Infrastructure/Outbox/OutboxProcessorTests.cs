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
    }

    [Fact]
    public async Task ClaimsDueMessagesWithALease()
    {
        Claim();

        var claimed = await CreateProcessor().ProcessBatchAsync(TestSupport.Ct);

        Assert.Equal(0, claimed);
        await store.Received(1).ClaimAsync(10, TestData.Now, TestData.Now.AddMinutes(5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulMessagesAreCompleted()
    {
        var message = Message(attempts: 1);
        Claim(message);

        var claimed = await CreateProcessor().ProcessBatchAsync(TestSupport.Ct);

        Assert.Equal(1, claimed);
        await handler.Received(1).HandleAsync(message, Arg.Any<CancellationToken>());
        await store.Received(1).CompleteAsync(message.Id, Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().RescheduleAsync(default, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task FailedMessagesAreRescheduledWithBackoff()
    {
        var message = Message(attempts: 2);
        Claim(message);
        handler.HandleAsync(message, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("smtp down"));

        await CreateProcessor().ProcessBatchAsync(TestSupport.Ct);

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

        await CreateProcessor().ProcessBatchAsync(TestSupport.Ct);

        await store.Received(1).DeadLetterAsync(message.Id, Arg.Is<string>(e => e.Contains("boom", StringComparison.Ordinal)), TestData.Now, Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().RescheduleAsync(default, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task UnknownTypesAreRetriedNotDropped()
    {
        var message = Message(attempts: 1, type: "from.a.newer.release");
        Claim(message);

        await CreateProcessor().ProcessBatchAsync(TestSupport.Ct);

        await store.Received(1).RescheduleAsync(message.Id, TestData.Now.AddSeconds(10), Arg.Is<string>(e => e.Contains("from.a.newer.release", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MessagesThatOutlivedTheirAttemptsAreNotRunAgain()
    {
        var message = Message(attempts: 4);
        Claim(message);

        await CreateProcessor().ProcessBatchAsync(TestSupport.Ct);

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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProcessor().ProcessBatchAsync(shutdown.Token));

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

    private void Claim(params OutboxEnvelope[] messages) =>
        store.ClaimAsync(default, default, default, TestSupport.Ct).ReturnsForAnyArgs(messages);

    private OutboxProcessor CreateProcessor() =>
        new(store, [handler], Options.Create(options), time, NullLogger<OutboxProcessor>.Instance);
}
