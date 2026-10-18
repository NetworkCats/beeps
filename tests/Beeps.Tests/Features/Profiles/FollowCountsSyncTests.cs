using System.Text.Json;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Profiles;

public sealed class FollowCountsSyncTests
{
    private readonly IProfileStore store = Substitute.For<IProfileStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();

    [Fact]
    public async Task TheHumanAndEveryoneOnTheOtherSideAreCountedAgainInSortedBatches()
    {
        var banned = Guid.NewGuid();
        List<Guid> people = [.. Enumerable.Range(0, FollowCountsHandler.BatchSize + 10).Select(_ => Guid.NewGuid())];
        store.ListFollowCounterpartsAsync(banned, Arg.Any<CancellationToken>()).Returns(people);
        List<Guid[]> batches = [];
        await store.RecountFollowsAsync(Arg.Do<IReadOnlyList<Guid>>(b => batches.Add([.. b])), Arg.Any<CancellationToken>());

        await new FollowCountsHandler(store, unitOfWork).HandleAsync(Envelope(new FollowCountsRequest(banned)), TestSupport.Ct);

        // Each batch in a transaction of its own, locked in id order as follows lock.
        Assert.Equal([FollowCountsHandler.BatchSize, 11], batches.Select(b => b.Length));
        Assert.Equal(batches.Count, unitOfWork.Locks.Count);
        Assert.Equal([.. people.Append(banned).Order()], batches.SelectMany(b => b));
    }

    [Fact]
    public void ARecountIsQueuedForTheHuman()
    {
        var outbox = Substitute.For<IOutbox>();
        var human = Guid.NewGuid();

        outbox.RecountFollows(human);

        outbox.Received(1).Enqueue(FollowCountsRequest.MessageType, new FollowCountsRequest(human), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<FollowCountsRequest>>());
    }

    private static OutboxEnvelope Envelope(FollowCountsRequest payload) =>
        new(Guid.NewGuid(), FollowCountsRequest.MessageType, JsonSerializer.Serialize(payload, ProfilesJsonContext.Default.FollowCountsRequest), 1, TestData.Now);
}
