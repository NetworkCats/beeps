using Beeps.Features.Beeps;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Beeps;

public sealed class BeepCounterFlushJobTests
{
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();
    private readonly IBeepStore beeps = Substitute.For<IBeepStore>();

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

    private BeepCounterFlushJob Create() => new(counters, beeps, NullLogger<BeepCounterFlushJob>.Instance);
}
