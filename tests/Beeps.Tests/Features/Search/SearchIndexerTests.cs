using Beeps.Features.Accounts;
using Beeps.Features.Search;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Search;

public sealed class SearchIndexerTests
{
    private readonly ISearchIndex index = Substitute.For<ISearchIndex>();
    private readonly ISearchStore store = Substitute.For<ISearchStore>();
    private readonly ISearchRebuild rebuild = Substitute.For<ISearchRebuild>();

    public SearchIndexerTests() => index.StartRebuildAsync(Arg.Any<CancellationToken>()).Returns(rebuild);

    [Fact]
    public async Task ARebuildCopiesEverythingSwapsAndCatchesUp()
    {
        var actors = new List<ActorDocument> { new(Guid.NewGuid(), ActorKind.Human, "tom", "Tom", null) };
        var full = Enumerable.Range(0, SearchIndexer.BatchSize).Select(_ => Beep()).ToList();
        var rest = new List<BeepDocument> { Beep() };
        store.ListActorsAsync(null, SearchIndexer.BatchSize, Arg.Any<CancellationToken>()).Returns(actors);
        store.ListBeepsAsync(null, null, SearchIndexer.BatchSize, Arg.Any<CancellationToken>()).Returns(full);
        var lastOfFull = full[^1].Id;
        store.ListBeepsAsync(null, lastOfFull, SearchIndexer.BatchSize, Arg.Any<CancellationToken>()).Returns(rest);
        // Posted while the copy ran.
        var late = new List<BeepDocument> { Beep() };
        store.ListBeepsAsync(null, Arg.Is<Guid?>(g => g != null && g != lastOfFull), SearchIndexer.BatchSize, Arg.Any<CancellationToken>()).Returns(late);

        var result = await new SearchIndexer(index, store, TestData.Time(), NullLogger<SearchIndexer>.Instance).RebuildAsync(TestSupport.Ct);

        Assert.Equal(new SearchRebuildResult(1, SearchIndexer.BatchSize + 1), result);
        Received.InOrder(() =>
        {
            rebuild.AddActorsAsync(actors, Arg.Any<CancellationToken>());
            rebuild.AddBeepsAsync(full, Arg.Any<CancellationToken>());
            rebuild.AddBeepsAsync(rest, Arg.Any<CancellationToken>());
            rebuild.CompleteAsync(Arg.Any<CancellationToken>());
            index.UpsertActorsAsync(actors, Arg.Any<CancellationToken>());
            index.UpsertBeepsAsync(late, Arg.Any<CancellationToken>());
        });
        await rebuild.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task AFailedCopyLeavesTheIndexesInUse()
    {
        store.ListActorsAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs(Task.FromException<IReadOnlyList<ActorDocument>>(new TimeoutException()));

        await Assert.ThrowsAsync<TimeoutException>(() => new SearchIndexer(index, store, TestData.Time(), NullLogger<SearchIndexer>.Instance).RebuildAsync(TestSupport.Ct));

        await rebuild.DidNotReceiveWithAnyArgs().CompleteAsync(TestSupport.Ct);
        await rebuild.Received(1).DisposeAsync();
    }

    private static BeepDocument Beep() => new(Guid.CreateVersion7(), "hi", Guid.NewGuid(), [], TestData.Now);
}
