using Beeps.Features.Admin;
using Beeps.Features.Moderation;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Admin;

public sealed class SystemServiceTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly ISystemStore store = Substitute.For<ISystemStore>();
    private readonly IModerationStore moderation = Substitute.For<IModerationStore>();
    private readonly IAdminStore admins = Substitute.For<IAdminStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly List<AdminAuditLog> audit = [];

    public SystemServiceTests()
    {
        admins.IsActiveAdminAsync(AdminId, Arg.Any<CancellationToken>()).Returns(true);
        admins.When(s => s.AddAuditLog(Arg.Any<AdminAuditLog>())).Do(ci => audit.Add(ci.Arg<AdminAuditLog>()));
    }

    [Fact]
    public async Task EveryMethodChecksTheCallerIsAnAdmin()
    {
        var service = Create();
        var outsider = Guid.NewGuid();

        Error?[] errors =
        [
            (await service.GetOverviewAsync(outsider, TestSupport.Ct)).Error,
            (await service.ListDeadJobsAsync(outsider, null, TestSupport.Ct)).Error,
            (await service.ListScheduledJobsAsync(outsider, TestSupport.Ct)).Error,
            (await service.RetryDeadJobAsync(outsider, Guid.NewGuid(), TestSupport.Ct)).Error,
            (await service.DiscardDeadJobAsync(outsider, Guid.NewGuid(), TestSupport.Ct)).Error,
        ];

        Assert.All(errors, e => Assert.Equal(AdminErrorCodes.NotAdmin, e?.Code));
        Assert.Empty(audit);
    }

    [Fact]
    public async Task TheOverviewCountsTodayFromMidnightUtc()
    {
        var midnight = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        store.CountAsync(midnight, Arg.Any<CancellationToken>()).Returns(new SiteCounts(10, 20, 300, 1, 2, 30, 4, 5, 6));
        moderation.CountQueueAsync(Arg.Any<CancellationToken>()).Returns(7);

        var result = await Create().GetOverviewAsync(AdminId, TestSupport.Ct);

        Assert.Equal(new AdminOverview(midnight, 10, 20, 300, 1, 2, 30, 7, 4, 5, 6), result.Value);
    }

    [Fact]
    public async Task DeadJobsAndScheduledJobsAreListed()
    {
        var before = Guid.NewGuid();
        var page = new CursorPage<DeadJobRow>([new DeadJobRow(Guid.NewGuid(), "media.process", 8, "IOException: disk", TestData.Now, TestData.Now)], null);
        store.ListDeadJobsAsync(before, SystemService.PageSize, Arg.Any<CancellationToken>()).Returns(page);
        store.ListScheduledJobsAsync(Arg.Any<CancellationToken>()).Returns([new ScheduledJobRow("media.cleanup", TestData.Now, TestData.Now, null)]);
        var service = Create();

        var dead = await service.ListDeadJobsAsync(AdminId, before, TestSupport.Ct);
        var scheduled = await service.ListScheduledJobsAsync(AdminId, TestSupport.Ct);

        Assert.Same(page, dead.Value);
        Assert.Equal("media.cleanup", Assert.Single(scheduled.Value).Name);
    }

    [Fact]
    public async Task RetryingAndDiscardingAreRecorded()
    {
        var (retried, discarded) = (Guid.NewGuid(), Guid.NewGuid());
        store.RetryDeadJobAsync(retried, TestData.Now, Arg.Any<CancellationToken>()).Returns(true);
        store.DeleteDeadJobAsync(discarded, Arg.Any<CancellationToken>()).Returns(true);
        var service = Create();

        var retry = await service.RetryDeadJobAsync(AdminId, retried, TestSupport.Ct);
        var discard = await service.DiscardDeadJobAsync(AdminId, discarded, TestSupport.Ct);

        Assert.True(retry.IsSuccess);
        Assert.True(discard.IsSuccess);
        Assert.Equal(
            [(AuditActions.RetryJob, AuditTargets.Job, retried), (AuditActions.DiscardJob, AuditTargets.Job, discarded)],
            audit.Select(a => (a.Action, a.TargetType, a.TargetId!.Value)));
        Assert.Equal(2, unitOfWork.Saves);
    }

    [Fact]
    public async Task AJobThatIsGoneIsNotFoundAndNotRecorded()
    {
        var result = await Create().RetryDeadJobAsync(AdminId, Guid.NewGuid(), TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
        Assert.Empty(audit);
        Assert.Equal(1, unitOfWork.Rollbacks);
    }

    private SystemService Create()
    {
        var localizer = new TestLocalizer();
        return new SystemService(store, moderation, new AdminAccess(admins, TestData.Time(), localizer), unitOfWork, TestData.Time(), localizer, NullLogger<SystemService>.Instance);
    }
}
