using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Beeps;

public sealed class BeepCounterFlushJobTests
{
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();
    private readonly IBeepStore beeps = Substitute.For<IBeepStore>();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();

    [Fact]
    public async Task WaitingViewsAreAddedToTheirColumnsAndPutBackIfThatFails()
    {
        var views = new Dictionary<Guid, long> { [Guid.NewGuid()] = 3 };
        counters.TakeDirtyAsync(default, TestSupport.Ct).ReturnsForAnyArgs([]);
        counters.TakeViewsAsync(Arg.Any<CancellationToken>()).Returns(views);

        await Create().RunAsync(TestSupport.Ct);
        beeps.AddViewsAsync(views, Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("down")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create().RunAsync(TestSupport.Ct));

        await beeps.Received(2).AddViewsAsync(views, Arg.Any<CancellationToken>());
        await counters.Received(1).ReturnViewsAsync(views, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryDirtyBeepIsRecountedAndPutBack()
    {
        IReadOnlyList<Guid> full = [.. Enumerable.Range(0, BeepCounterFlushJob.BatchSize).Select(_ => Guid.NewGuid())];
        IReadOnlyList<Guid> rest = [Guid.NewGuid()];
        counters.TakeDirtyAsync(BeepCounterFlushJob.BatchSize, Arg.Any<CancellationToken>()).Returns(full, rest);
        var recounted = new Dictionary<Guid, BeepCounts> { [rest[0]] = new(2, 1) };
        beeps.RecountAsync(rest, Arg.Any<CancellationToken>()).Returns(recounted);

        await Create().RunAsync(TestSupport.Ct);

        await beeps.Received(1).RecountAsync(full, Arg.Any<CancellationToken>());
        await counters.Received(1).SetAsync(recounted, Arg.Any<CancellationToken>());
        await counters.Received(2).TakeDirtyAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACorrectedCountGoesToOpenPages()
    {
        var (drifted, right) = (Guid.NewGuid(), Guid.NewGuid());
        IReadOnlyList<Guid> batch = [drifted, right];
        counters.TakeDirtyAsync(BeepCounterFlushJob.BatchSize, Arg.Any<CancellationToken>()).Returns(batch, []);
        counters.GetAsync(batch, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, BeepCounts> { [drifted] = new(5, 0), [right] = new(1, 1) });
        beeps.RecountAsync(batch, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, BeepCounts> { [drifted] = new(4, 0), [right] = new(1, 1) });

        await Create().RunAsync(TestSupport.Ct);

        await liveEvents.Received(1).MarkCountsChangedAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { drifted })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NothingDirtyMeansNoQuery()
    {
        counters.TakeDirtyAsync(default, TestSupport.Ct).ReturnsForAnyArgs([]);

        await Create().RunAsync(TestSupport.Ct);

        await beeps.DidNotReceiveWithAnyArgs().RecountAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task AFailedBatchIsMarkedDirtyAgain()
    {
        IReadOnlyList<Guid> batch = [Guid.NewGuid()];
        counters.TakeDirtyAsync(default, TestSupport.Ct).ReturnsForAnyArgs(batch);
        beeps.RecountAsync(batch, Arg.Any<CancellationToken>()).Returns<IReadOnlyDictionary<Guid, BeepCounts>>(_ => throw new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create().RunAsync(TestSupport.Ct));

        await counters.Received(1).MarkDirtyAsync(batch, Arg.Any<CancellationToken>());
    }

    private BeepCounterFlushJob Create() => new(counters, beeps, TestData.Live(liveEvents), NullLogger<BeepCounterFlushJob>.Instance);
}
