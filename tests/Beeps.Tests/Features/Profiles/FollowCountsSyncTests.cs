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
        List<Guid> followers = [.. Enumerable.Range(0, FollowCountsHandler.BatchSize).Select(_ => Guid.NewGuid())];
        List<Guid> followed = [.. Enumerable.Range(0, 10).Select(_ => Guid.NewGuid())];
        store.ListFollowCounterpartsAsync(banned, Arg.Any<CancellationToken>()).Returns(new FollowCounterparts(followers, followed));
        List<(Guid[] Following, Guid[] Followers)> batches = [];
        await store.RecountFollowsAsync(
            Arg.Do<IReadOnlyCollection<Guid>>(f => batches.Add(([.. f], []))),
            Arg.Do<IReadOnlyCollection<Guid>>(f => batches[^1] = (batches[^1].Following, [.. f])),
            Arg.Any<CancellationToken>());

        await new FollowCountsHandler(store, unitOfWork).HandleAsync(Envelope(new FollowCountsRequest(banned)), TestSupport.Ct);

        // Each batch in a transaction of its own, locked in id order as follows lock.
        var all = batches.Select(b => b.Following.Union(b.Followers).Order().ToArray()).ToList();
        Assert.Equal([FollowCountsHandler.BatchSize, 11], all.Select(b => b.Length));
        Assert.Equal(batches.Count, unitOfWork.Locks.Count);
        Assert.Equal([.. followers.Concat(followed).Append(banned).Order()], all.SelectMany(b => b));
        // Only the count that changed: whom their followers follow, who follows those they follow; both for them.
        Assert.Equal([.. followers.Append(banned).Order()], batches.SelectMany(b => b.Following).Order());
        Assert.Equal([.. followed.Append(banned).Order()], batches.SelectMany(b => b.Followers).Order());
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
