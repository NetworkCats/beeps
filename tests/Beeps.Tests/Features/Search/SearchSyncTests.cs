using Beeps.Features.Accounts;
using Beeps.Features.Live;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Search;

public sealed class SearchSyncTests
{
    private readonly ISearchStore store = Substitute.For<ISearchStore>();
    private readonly ISearchIndex index = Substitute.For<ISearchIndex>();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();

    [Fact]
    public async Task AVisibleBeepIsIndexedAndAnyOtherIsDropped()
    {
        var shown = new BeepDocument(Guid.NewGuid(), "hi #Beeps", Guid.NewGuid(), ["beeps"], TestData.Now);
        var gone = Guid.NewGuid();
        store.FindBeepAsync(shown.Id, Arg.Any<CancellationToken>()).Returns(shown);
        var handler = new SearchBeepSyncHandler(store, index, TestData.Live(liveEvents));

        await handler.HandleAsync(Envelope(new SearchBeepSyncRequest(shown.Id)), TestSupport.Ct);
        await handler.HandleAsync(Envelope(new SearchBeepSyncRequest(gone)), TestSupport.Ct);

        await index.Received(1).UpsertBeepsAsync(Arg.Is<IReadOnlyCollection<BeepDocument>>(d => d.Single() == shown), Arg.Any<CancellationToken>());
        await index.Received(1).DeleteBeepsAsync(Arg.Is<IReadOnlyCollection<Guid>>(d => d.Single() == gone), Arg.Any<CancellationToken>());
        // Open search pages ask again each time.
        Assert.Equal(2, TestData.LiveEvents(liveEvents, LiveEventTypes.SearchChanged).Count);
    }

    [Fact]
    public async Task ABeepDeletedWhileItWasIndexedIsTakenOutAgain()
    {
        var beep = new BeepDocument(Guid.NewGuid(), "hi", Guid.NewGuid(), [], TestData.Now);
        // Read as shown; deleted (and its delete sent by another sync) before this one's write went in.
        store.FindBeepAsync(beep.Id, Arg.Any<CancellationToken>()).Returns(beep, (BeepDocument?)null);

        await new SearchBeepSyncHandler(store, index, TestData.Live(liveEvents)).HandleAsync(Envelope(new SearchBeepSyncRequest(beep.Id)), TestSupport.Ct);

        Received.InOrder(() =>
        {
            index.UpsertBeepsAsync(Arg.Any<IReadOnlyCollection<BeepDocument>>(), Arg.Any<CancellationToken>());
            index.DeleteBeepsAsync(Arg.Is<IReadOnlyCollection<Guid>>(d => d.Single() == beep.Id), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task AnAccountBannedWhileItWasIndexedIsTakenOutAgain()
    {
        var tom = Actor("tom");
        store.GetAccountAsync(tom.Id, Arg.Any<CancellationToken>())
            .Returns([new ActorDocumentState(tom.Id, tom)], [new ActorDocumentState(tom.Id, null)]);
        store.ListBeepsAsync(default, default, default, TestSupport.Ct).ReturnsForAnyArgs([]);

        await new SearchAccountSyncHandler(store, index, TestData.Live(liveEvents)).HandleAsync(Envelope(new SearchAccountSyncRequest(tom.Id, Beeps: true)), TestSupport.Ct);

        Received.InOrder(() =>
        {
            index.UpsertActorsAsync(Arg.Any<IReadOnlyCollection<ActorDocument>>(), Arg.Any<CancellationToken>());
            index.DeleteActorsAsync(Arg.Is<IReadOnlyCollection<Guid>>(d => d.Single() == tom.Id), Arg.Any<CancellationToken>());
            index.DeleteBeepsByAuthorsAsync(Arg.Is<IReadOnlyCollection<Guid>>(d => d.Single() == tom.Id), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task AProfileChangeUpdatesOnlyTheProfiles()
    {
        var tom = Actor("tom");
        var deletedAgent = Guid.NewGuid();
        store.GetAccountAsync(tom.Id, Arg.Any<CancellationToken>()).Returns([new ActorDocumentState(tom.Id, tom), new ActorDocumentState(deletedAgent, null)]);

        await new SearchAccountSyncHandler(store, index, TestData.Live(liveEvents)).HandleAsync(Envelope(new SearchAccountSyncRequest(tom.Id, Beeps: false)), TestSupport.Ct);

        await index.Received(1).UpsertActorsAsync(Arg.Is<IReadOnlyCollection<ActorDocument>>(d => d.Single() == tom), Arg.Any<CancellationToken>());
        await index.Received(1).DeleteActorsAsync(Arg.Is<IReadOnlyCollection<Guid>>(d => d.Single() == deletedAgent), Arg.Any<CancellationToken>());
        await index.DidNotReceiveWithAnyArgs().DeleteBeepsByAuthorsAsync(default!, TestSupport.Ct);
        await store.DidNotReceiveWithAnyArgs().ListBeepsAsync(default, default, default, TestSupport.Ct);
        Assert.Single(TestData.LiveEvents(liveEvents, LiveEventTypes.SearchChanged));
    }

    [Fact]
    public async Task ABanTakesTheAccountAndAllItsBeepsOut()
    {
        var tom = Guid.NewGuid();
        var agent = Guid.NewGuid();
        store.GetAccountAsync(tom, Arg.Any<CancellationToken>()).Returns([new ActorDocumentState(tom, null), new ActorDocumentState(agent, null)]);

        await new SearchAccountSyncHandler(store, index, TestData.Live(liveEvents)).HandleAsync(Envelope(new SearchAccountSyncRequest(tom, Beeps: true)), TestSupport.Ct);

        await index.Received(1).DeleteActorsAsync(Arg.Is<IReadOnlyCollection<Guid>>(d => d.SequenceEqual(new[] { tom, agent })), Arg.Any<CancellationToken>());
        await index.Received(1).DeleteBeepsByAuthorsAsync(Arg.Is<IReadOnlyCollection<Guid>>(d => d.SequenceEqual(new[] { tom, agent })), Arg.Any<CancellationToken>());
        await index.DidNotReceiveWithAnyArgs().UpsertBeepsAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task AnUnbanPutsEveryBeepBackInBatches()
    {
        var tom = Actor("tom");
        store.GetAccountAsync(tom.Id, Arg.Any<CancellationToken>()).Returns([new ActorDocumentState(tom.Id, tom)]);
        var full = Enumerable.Range(0, SearchAccountSyncHandler.BatchSize).Select(_ => Beep(tom.Id)).ToList();
        var rest = new List<BeepDocument> { Beep(tom.Id) };
        store.ListBeepsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), null, SearchAccountSyncHandler.BatchSize, Arg.Any<CancellationToken>()).Returns(full);
        store.ListBeepsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), full[^1].Id, SearchAccountSyncHandler.BatchSize, Arg.Any<CancellationToken>()).Returns(rest);

        await new SearchAccountSyncHandler(store, index, TestData.Live(liveEvents)).HandleAsync(Envelope(new SearchAccountSyncRequest(tom.Id, Beeps: true)), TestSupport.Ct);

        await index.Received(1).UpsertBeepsAsync(full, Arg.Any<CancellationToken>());
        await index.Received(1).UpsertBeepsAsync(rest, Arg.Any<CancellationToken>());
        await index.DidNotReceiveWithAnyArgs().DeleteBeepsByAuthorsAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public void SyncJobsGoThroughTheOutbox()
    {
        var outbox = Substitute.For<IOutbox>();
        var id = Guid.NewGuid();

        outbox.SyncBeepToSearch(id);
        outbox.SyncAccountToSearch(id, beeps: true);

        outbox.Received(1).Enqueue(SearchBeepSyncRequest.MessageType, new SearchBeepSyncRequest(id), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<SearchBeepSyncRequest>>());
        outbox.Received(1).Enqueue(SearchAccountSyncRequest.MessageType, new SearchAccountSyncRequest(id, true), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<SearchAccountSyncRequest>>());
    }

    private static ActorDocument Actor(string handle) => new(Guid.NewGuid(), ActorKind.Human, handle, handle, null);

    private static BeepDocument Beep(Guid author) => new(Guid.CreateVersion7(), "hi", author, [], TestData.Now);

    private static OutboxEnvelope Envelope(SearchBeepSyncRequest payload) =>
        TestData.Envelope(SearchBeepSyncRequest.MessageType, payload, SearchJsonContext.Default.SearchBeepSyncRequest);

    private static OutboxEnvelope Envelope(SearchAccountSyncRequest payload) =>
        TestData.Envelope(SearchAccountSyncRequest.MessageType, payload, SearchJsonContext.Default.SearchAccountSyncRequest);
}
