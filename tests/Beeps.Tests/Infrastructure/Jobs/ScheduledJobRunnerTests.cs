using Beeps.Features.Shared;
using Beeps.Infrastructure.Jobs;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace Beeps.Tests.Infrastructure.Jobs;

public sealed class ScheduledJobRunnerTests
{
    private readonly IScheduledJob job = Substitute.For<IScheduledJob>();
    private readonly IScheduledJobStore store = Substitute.For<IScheduledJobStore>();
    private readonly IScheduledJobLease lease = Substitute.For<IScheduledJobLease>();

    public ScheduledJobRunnerTests()
    {
        job.Name.Returns("test.job");
        job.Interval.Returns(TimeSpan.FromHours(1));
        store.TryLockAsync("test.job", Arg.Any<CancellationToken>()).Returns(lease);
    }

    [Fact]
    public async Task ADueJobRunsAndIsRecorded()
    {
        lease.GetLastStartedAsync(Arg.Any<CancellationToken>()).Returns(TestData.Now.AddHours(-1));

        await CreateRunner().RunDueAsync(TestSupport.Ct);

        Received.InOrder(() =>
        {
            lease.MarkStartedAsync(TestData.Now, Arg.Any<CancellationToken>());
            job.RunAsync(Arg.Any<CancellationToken>());
            lease.MarkFinishedAsync(TestData.Now, null, Arg.Any<CancellationToken>());
        });
        await lease.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ANeverRunJobIsDue()
    {
        await CreateRunner().RunDueAsync(TestSupport.Ct);

        await job.Received(1).RunAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AJobRunRecentlyByAnyInstanceIsSkipped()
    {
        lease.GetLastStartedAsync(Arg.Any<CancellationToken>()).Returns(TestData.Now.AddMinutes(-59));

        await CreateRunner().RunDueAsync(TestSupport.Ct);

        await job.DidNotReceiveWithAnyArgs().RunAsync(TestSupport.Ct);
        await lease.DidNotReceiveWithAnyArgs().MarkStartedAsync(default, TestSupport.Ct);
        await lease.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task AJobRunningElsewhereIsSkipped()
    {
        store.TryLockAsync("test.job", Arg.Any<CancellationToken>()).Returns((IScheduledJobLease?)null);

        await CreateRunner().RunDueAsync(TestSupport.Ct);

        await job.DidNotReceiveWithAnyArgs().RunAsync(TestSupport.Ct);
    }

    [Fact]
    public async Task AFailureIsRecordedAndDoesNotStopOtherJobs()
    {
        job.RunAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
        var other = Substitute.For<IScheduledJob>();
        other.Name.Returns("other.job");
        other.Interval.Returns(TimeSpan.FromHours(1));
        store.TryLockAsync("other.job", Arg.Any<CancellationToken>()).Returns(Substitute.For<IScheduledJobLease>());

        await CreateRunner(job, other).RunDueAsync(TestSupport.Ct);

        await lease.Received(1).MarkFinishedAsync(TestData.Now, "InvalidOperationException: boom", Arg.Any<CancellationToken>());
        await other.Received(1).RunAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShutdownIsNotRecordedAsAFailure()
    {
        using var shutdown = new CancellationTokenSource();
        job.RunAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateRunner().RunDueAsync(shutdown.Token));

        await lease.DidNotReceiveWithAnyArgs().MarkFinishedAsync(default, default, TestSupport.Ct);
        await lease.Received(1).DisposeAsync();
    }

    private ScheduledJobRunner CreateRunner(params IScheduledJob[] jobs) =>
        new(jobs.Length == 0 ? [job] : jobs, store, TestData.Time(), NullLogger<ScheduledJobRunner>.Instance);
}
