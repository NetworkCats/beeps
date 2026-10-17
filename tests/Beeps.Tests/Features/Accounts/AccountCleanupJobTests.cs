using Beeps.Features.Accounts;
using Beeps.Features.Live;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Accounts;

public sealed class AccountCleanupJobTests
{
    private readonly ISessionStore sessions = Substitute.For<ISessionStore>();
    private readonly IAccountStore accounts = Substitute.For<IAccountStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();

    public AccountCleanupJobTests() =>
        accounts.ReleaseUnverifiedAsync(default, default, default, TestSupport.Ct).ReturnsForAnyArgs(Array.Empty<Guid>());

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
    public async Task ReleasesSignUpsUnverifiedForAWeekAndEndsTheirSessionsAtOnce()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        accounts.ReleaseUnverifiedAsync(default, default, default, TestSupport.Ct).ReturnsForAnyArgs([first, second], []);

        await CreateJob().RunAsync(TestSupport.Ct);

        await accounts.Received(1).ReleaseUnverifiedAsync(TestData.Now - AccountCleanupJob.UnverifiedRetention, TestData.Now, AccountCleanupJob.BatchSize, Arg.Any<CancellationToken>());
        Assert.Equal([null], unitOfWork.Locks);
        await sessions.Received(1).ForgetAllAsync(first, Arg.Any<CancellationToken>());
        await sessions.Received(1).ForgetAllAsync(second, Arg.Any<CancellationToken>());
        // Their open tabs sign out.
        Assert.Equal([$$"""{"account_id":"{{first:D}}"}""", $$"""{"account_id":"{{second:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.SessionsEnded));
        await accounts.Received(1).ForgetAccountAsync(first, Arg.Any<CancellationToken>());
        await accounts.Received(1).ForgetAccountAsync(second, Arg.Any<CancellationToken>());
    }

    private AccountCleanupJob CreateJob() => new(sessions, accounts, unitOfWork, TestData.Live(liveEvents), TestData.Time(), NullLogger<AccountCleanupJob>.Instance);
}
