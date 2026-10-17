using Beeps.Features.Live;
using Beeps.Infrastructure.Live;

namespace Beeps.Tests.Infrastructure.Live;

/// <summary>What counts as gone from the stream, from the numbers <c>XINFO STREAM</c> gives (checked against Valkey 9).</summary>
public sealed class RedisLiveEventStoreTests
{
    [Fact]
    public void NothingRemovedNothingGone() =>
        Assert.Null(RedisLiveEventStore.TrimmedUpTo(entriesAdded: 3, length: 3, "100-0", "102-0", "0-0"));

    [Fact]
    public void TrimmingByLengthLeavesNoDeletedIdSoEverythingBeforeTheFirstEntryCounts()
    {
        // XTRIM / XADD MAXLEN: max-deleted-entry-id stays 0-0.
        Assert.Equal(LiveEventId.Parse("102-4"), RedisLiveEventStore.TrimmedUpTo(entriesAdded: 5, length: 2, "102-5", "103-0", "0-0"));
        Assert.Equal(new LiveEventId(101, long.MaxValue), RedisLiveEventStore.TrimmedUpTo(entriesAdded: 5, length: 2, "102-0", "103-0", "0-0"));
    }

    [Fact]
    public void AStreamTrimmedEmptyLostEverythingUpToItsLastId() =>
        Assert.Equal(LiveEventId.Parse("103-0"), RedisLiveEventStore.TrimmedUpTo(entriesAdded: 5, length: 0, null, "103-0", "0-0"));

    [Fact]
    public void ADeletedEntryNewerThanTheFirstOneCountsToo() =>
        Assert.Equal(LiveEventId.Parse("102-0"), RedisLiveEventStore.TrimmedUpTo(entriesAdded: 5, length: 4, "100-0", "103-0", "102-0"));
}
