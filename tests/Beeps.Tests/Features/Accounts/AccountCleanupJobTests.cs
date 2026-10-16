using Beeps.Features.Accounts;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Accounts;

public sealed class AccountCleanupJobTests
{
    private readonly ISessionStore sessions = Substitute.For<ISessionStore>();
    private readonly IAccountStore accounts = Substitute.For<IAccountStore>();

    [Fact]
    public async Task DeletesSessionsEndedLongerAgoThanTheRetentionAndExpiredCodes()
    {
        await CreateJob().RunAsync(TestSupport.Ct);

        await sessions.Received(1).DeleteEndedAsync(TestData.Now - AccountCleanupJob.EndedSessionRetention, AccountCleanupJob.BatchSize, Arg.Any<CancellationToken>());
        await accounts.Received(1).DeleteExpiredEmailCodesAsync(TestData.Now, AccountCleanupJob.BatchSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsDeletingWhileBatchesAreFull()
    {
        sessions.DeleteEndedAsync(default, default, TestSupport.Ct)
            .ReturnsForAnyArgs(AccountCleanupJob.BatchSize, AccountCleanupJob.BatchSize, 3);

        await CreateJob().RunAsync(TestSupport.Ct);

        await sessions.ReceivedWithAnyArgs(3).DeleteEndedAsync(default, default, TestSupport.Ct);
        await accounts.ReceivedWithAnyArgs(1).DeleteExpiredEmailCodesAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public void RunsHourly()
    {
        var job = CreateJob();

        Assert.Equal(AccountCleanupJob.JobName, job.Name);
        Assert.Equal(TimeSpan.FromHours(1), job.Interval);
    }

    private AccountCleanupJob CreateJob() => new(sessions, accounts, TestData.Time(), NullLogger<AccountCleanupJob>.Instance);
}
