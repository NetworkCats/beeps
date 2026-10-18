using Beeps.Features.Admin;
using Beeps.Features.Live;
using Beeps.Features.Moderation;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Live;
using Beeps.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Live;

public sealed class AdminFiguresWorkerTests
{
    private readonly ISystemStore system = Substitute.For<ISystemStore>();
    private readonly IModerationStore moderation = Substitute.For<IModerationStore>();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();
    private readonly AdminWatchers watchers = new();
    private int outbox;

    public AdminFiguresWorkerTests()
    {
        system.CountUnannouncedAsync(default, TestSupport.Ct).ReturnsForAnyArgs(_ => new UnannouncedCounts(0, 0, outbox, 0));
        system.ListDeadJobsAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<DeadJobRow>([], null));
        system.ListScheduledJobsAsync(TestSupport.Ct).ReturnsForAnyArgs([]);
    }

    [Fact]
    public async Task FiguresWithNoEventsAreWatchedOnlyWhileAnAdminPageIsOpenAndAnnouncedWhenTheyMove()
    {
        using var worker = Create();
        await worker.StartAsync(TestSupport.Ct);

        await Task.Delay(100, TestSupport.Ct);
        await system.DidNotReceiveWithAnyArgs().CountUnannouncedAsync(default, TestSupport.Ct);

        using (watchers.Watch())
        {
            // The first look: the page that just opened may be older than what it finds.
            await WaitUntilAsync(() => TestData.LiveEvents(liveEvents, LiveEventTypes.AdminChanged).Count == 1);
            await Task.Delay(100, TestSupport.Ct);
            Assert.Single(TestData.LiveEvents(liveEvents, LiveEventTypes.AdminChanged));

            // The outbox backlog grows: no event says so, the worker does.
            outbox = 3;
            await WaitUntilAsync(() => TestData.LiveEvents(liveEvents, LiveEventTypes.AdminChanged).Count == 2);
            await Task.Delay(100, TestSupport.Ct);
            Assert.Equal(2, TestData.LiveEvents(liveEvents, LiveEventTypes.AdminChanged).Count);
        }

        await worker.StopAsync(TestSupport.Ct);
    }

    private AdminFiguresWorker Create()
    {
        var services = new ServiceCollection().AddSingleton(system).AddSingleton(moderation).BuildServiceProvider();
        return new AdminFiguresWorker(
            watchers,
            services.GetRequiredService<IServiceScopeFactory>(),
            TestData.Live(liveEvents),
            Options.Create(new LiveOptions { AdminFiguresInterval = TimeSpan.FromMilliseconds(10) }),
            TimeProvider.System,
            NullLogger<AdminFiguresWorker>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10, TestSupport.Ct);
        }

        Assert.True(condition());
    }
}
